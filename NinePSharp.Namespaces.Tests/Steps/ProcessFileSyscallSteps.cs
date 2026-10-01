using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Native regular file syscalls")]
public sealed class ProcessFileSyscallSteps
{
    private readonly FileSyscallFixture fixture = new();
    private int fd;
    private ReadOnlyMemory<byte> read;
    private VProcess child = null!;
    private int created;

    [When("native create opens that existing file again")]
    public async Task CreateExisting() => created = await fixture.Calls.CreateAsync("/file", new(0, 2));

    [Then("the newly created descriptor has length zero and the same resource identity")]
    public async Task CreatedSameFile()
    {
        var slots = fixture.Process.Descriptors.Snapshot();
        Assert.Equal(slots[fd].Handle.Resource.Identity, slots[created].Handle.Resource.Identity);
        Assert.Equal(0UL, (await fixture.Local.StatAsync(slots[created].Handle, default)).Length);
    }

    [When("exclusive create attempts that existing name")]
    public async Task ExclusiveCreate() => await Assert.ThrowsAsync<NamespaceException>(() =>
        fixture.Calls.CreateAsync("/file", new(0, NinePSharp.Constants.NinePConstants.OEXCL | 2)).AsTask());

    [Then("creation fails and the original contents remain abcdef")]
    public async Task ContentsPreserved() => Assert.Equal("abcdef"u8.ToArray(), (await fixture.Calls.ReadAsync(fd, 6)).ToArray());

    [Given("its descriptor table is full")]
    public void FillDescriptors()
    {
        for (int i = 1; i < 5000; i++)
            fixture.Process.Descriptors.Install(DescriptorGroupTests.Handle(i.ToString()), () => ValueTask.CompletedTask);
    }

    [When("native create attempts a new name")]
    public async Task FullTableCreate() => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        fixture.Calls.CreateAsync("/new", new(0x180, 2)).AsTask());

    [Then("descriptor allocation fails and the created handle is closed once")]
    public void CreatedHandleClosed() => Assert.Equal(1, fixture.Resources.ClunkCount);

    [Then("the new file still exists")]
    public async Task CreatedFileExists() => Assert.NotNull(await fixture.Resources.WalkAsync(fixture.Process.Root.Current, "new", default));

    [Given("a syscall process with an open file containing abcdef")]
    public async Task Open()
    {
        fd = await fixture.OpenAsync();
        await fixture.Calls.PWriteAsync(fd, 0, "abcdef"u8.ToArray());
    }

    [When("it reads two bytes through a duplicate descriptor")]
    public async Task ReadDuplicate()
    {
        int duplicate = await fixture.Process.Descriptors.DuplicateAsync(fd);
        read = await fixture.Calls.ReadAsync(duplicate, 2);
    }

    [When("it reads two bytes explicitly at offset three")]
    public async Task ReadPositioned() => read = await fixture.Calls.PReadAsync(fd, 3, 2);

    [Then("the syscall read returns (.*)")]
    public void ReadResult(string expected) => Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(read.Span));

    [Then("the original descriptor position is (.*)")]
    public async Task Position(long expected) => Assert.Equal(expected, await fixture.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));

    [When("an eight byte implicit write fails at the provider")]
    public async Task FailedWrite()
    {
        fixture.Plane.WriteOverride = (_, _, _, _, _) => throw new IOException("failed write");
        await Assert.ThrowsAsync<IOException>(() => fixture.Calls.WriteAsync(fd, new byte[8]).AsTask());
    }

    [Then("the syscall file remains open")]
    public void FileRemainsOpen()
    {
        Assert.Single(fixture.Process.Descriptors.Snapshot());
        Assert.Equal(0, fixture.Resources.ClunkCount);
    }

    [Given("a child sharing its descriptors")]
    public void Fork() => child = fixture.Table.Fork(fixture.Process.Id, NamespaceForkMode.Share);

    [When("its second open finishes after its exit")]
    public async Task ExitDuringOpen()
    {
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Plane.AfterOpen = _ => resume.Task;
        Task<int> opening = fixture.OpenAsync().AsTask();
        await fixture.Table.TerminateAsync(fixture.Process.Id);
        resume.SetResult();
        await Assert.ThrowsAsync<NamespaceException>(() => opening);
    }

    [Then("that open fails and its provider handle is closed once")]
    public void ClosedOnce() => Assert.Equal(1, fixture.Resources.ClunkCount);

    [Then("the child still has the original usable descriptor")]
    public async Task ChildRetainsDescriptor()
    {
        Assert.Single(child.Descriptors.Snapshot());
        Assert.Equal("abcdef"u8.ToArray(), (await fixture.ForProcess(child).ReadAsync(fd, 6)).ToArray());
    }

    [AfterScenario]
    public async Task Cleanup() => await fixture.DisposeAsync();
}

using System.Buffers.Binary;
using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Processes inspect native file metadata")]
public sealed class FileStatSyscallSteps
{
    private readonly FileStatFixture f = new();
    private readonly List<ReadOnlyMemory<byte>> replies = new();
    private ResourceHandle selected = null!;
    private ResourceHandle original = null!;
    private int descriptor;
    private ReadOnlyMemory<byte> result;

    [Given("a stat process with a new resource mounted on its root")]
    public void RootMount()
    {
        selected = f.Files.Resources.Directory("mounted");
        f.Files.Process.ProcessGroup.MountTable.Mount(selected, f.Files.Process.Root.Current);
    }

    [When("it stats the process root")]
    public async Task StatRoot() => result = await f.Calls.StatAsync("/", 4096);

    [Then("root metadata comes from the mounted resource with an empty visible name")]
    public void RootResult()
    {
        Assert.Equal(selected, Assert.Single(f.Requests).Resource);
        Assert.Equal(string.Empty, FileStatSyscallTests.Name(result));
    }

    [Then("stat allocates no descriptor")]
    public void NoDescriptor() => Assert.Empty(f.Files.Process.Descriptors.Snapshot());

    [Given("a stat process with other bound onto file and file opened")]
    public async Task Alias()
    {
        var root = f.Files.Process.Root.Current;
        original = (await f.Files.Resources.WalkAsync(root, "file", default))!;
        selected = (await f.Files.Resources.WalkAsync(root, "other", default))!;
        f.Files.Process.ProcessGroup.MountTable.Mount(NamespaceChannel.Restore(new[] { new ChannelFrame("/", selected) }), original);
        descriptor = await f.Calls.OpenAsync("/file", new(0));
    }

    [When("the file binding is removed")]
    public void Unmount() => f.Files.Process.ProcessGroup.MountTable.Unmount(original);

    [When("it stats the retained file descriptor")]
    public async Task Fstat() => result = await f.Calls.FStatAsync(descriptor, 4096);

    [Then("metadata comes from other with visible name file")]
    public void AliasResult()
    {
        Assert.Equal(selected, f.Requests[^1].Resource);
        Assert.Equal("file", FileStatSyscallTests.Name(result));
    }

    [Then("fresh path stat selects the original file")]
    public async Task FreshLookup()
    {
        await f.Calls.StatAsync("/file", 4096);
        Assert.Equal(original, f.Requests[^1].Resource);
    }

    [Given("a stat descriptor with provider size 80 and a visible name twelve bytes longer")]
    public void LongAlias()
    {
        descriptor = f.Files.Process.Descriptors.Install(
            new(f.Files.Process.Root.Current, "manual", 0, 0),
            () => ValueTask.CompletedTask,
            visibleName: "abcdefghijklmnop");
        f.Reply = (resource, count) =>
        {
            byte[] bytes = f.Record(resource, "name");
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes.Length > count ? bytes.AsMemory(0, 2) : bytes);
        };
    }

    [When("it requests stat capacities 2 then 80 then 92")]
    public async Task Sizes()
    {
        foreach (uint count in new uint[] { 2, 80, 92 })
        {
            replies.Add(await f.Calls.FStatAsync(descriptor, count));
        }
    }

    [Then("stat returns lengths 2 then 2 then 92 with size hints 78 then 90 then 90")]
    public void SizeResults()
    {
        Assert.Equal(new[] { 2, 2, 92 }, replies.Select(r => r.Length));
        Assert.Equal(new ushort[] { 78, 90, 90 }, replies.Select(r => BinaryPrimitives.ReadUInt16LittleEndian(r.Span)));
    }

    [Given("a write-only stat descriptor positioned at 73")]
    public async Task WriteOnly()
    {
        descriptor = await f.Calls.OpenAsync("/file", new(1));
        await f.Calls.SeekAsync(descriptor, 73, Plan9SeekWhence.Set);
    }

    [Then("the visible stat name is file and the shared position is still 73")]
    public async Task Position()
    {
        Assert.Equal("file", FileStatSyscallTests.Name(result));
        Assert.Equal(73, await f.Calls.SeekAsync(descriptor, 0, Plan9SeekWhence.Current));
    }

    [AfterScenario]
    public async Task Cleanup() => await f.DisposeAsync();
}

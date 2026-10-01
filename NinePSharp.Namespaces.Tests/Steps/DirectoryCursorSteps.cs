using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Metadata-backed directory syscall cursors")]
public sealed class DirectoryCursorSteps
{
    private readonly FileSyscallFixture fixture = new();
    private readonly List<string> names = new();
    private int fd;

    [Given("a syscall directory containing file and other")]
    public async Task Open() => fd = await fixture.Calls.OpenAsync("/", new(0));

    [Given("a syscall union directory with same,upper and same,lower")]
    public async Task OpenUnion()
    {
        var upper = fixture.Resources.Directory("upper", "same", "upper");
        var lower = fixture.Resources.Directory("lower", "same", "lower");
        var mounts = fixture.Process.ProcessGroup.MountTable;
        mounts.Mount(upper, fixture.Process.Root.Current);
        mounts.Mount(lower, fixture.Process.Root.Current, MountFlags.After);
        await Open();
    }

    [When("the original descriptor reads exactly the first stat record")]
    public async Task FirstRecord() => Add(await fixture.Calls.ReadAsync(fd, 68));

    [When("its duplicate reads the remaining stat records")]
    public async Task ReadDuplicate()
    {
        int duplicate = await fixture.Process.Descriptors.DuplicateAsync(fd);
        Add(await fixture.Calls.ReadAsync(duplicate, 4096));
    }

    [When("all its stat records have been read")]
    public async Task ReadAll() => Add(await fixture.Calls.ReadAsync(fd, 4096));

    [Then("the directory names returned are (.*)")]
    public void ReturnedNames(string expected) => Assert.Equal(expected.Split(','), names);

    [Then("the directory cursor is at EOF")]
    public async Task Eof() => Assert.Empty((await fixture.Calls.ReadAsync(fd, 4096)).ToArray());

    [When("a new entry is created and the directory is rewound")]
    public async Task RewindAfterCreate()
    {
        await fixture.Calls.CreateAsync("new", new(0x180, 2));
        await fixture.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
    }

    [Then("a fresh directory read returns (.*)")]
    public async Task FreshRead(string expected)
        => Assert.Equal(expected.Split(','), DirectoryCursorTests.Decode(await fixture.Calls.ReadAsync(fd, 4096)).Select(x => x.Name));

    [When("a read buffer is too small for the first stat record")]
    public async Task TooSmall() => await Assert.ThrowsAsync<NamespaceFidException>(() => fixture.Calls.ReadAsync(fd, 67).AsTask());

    [AfterScenario]
    public async Task Cleanup() => await fixture.DisposeAsync();

    private void Add(ReadOnlyMemory<byte> bytes) => names.AddRange(DirectoryCursorTests.Decode(bytes).Select(x => x.Name));
}

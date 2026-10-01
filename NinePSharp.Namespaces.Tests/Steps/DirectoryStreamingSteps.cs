using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Processes read native provider directory streams")]
public sealed class DirectoryStreamingSteps
{
    private readonly StreamingDirectoryFixture fixture = new();
    private readonly List<string> names = new();
    private readonly List<int> lengths = new();
    private int descriptor;

    [Given("a native directory with records first of 64 bytes and second of 72 bytes")]
    public async Task Plain() => descriptor = await fixture.Calls.OpenAsync("/", new(0));

    [Given("a native union whose members each contain the name same")]
    public async Task Union()
    {
        fixture.Union(fixture.Directory("A", fixture.Record("same", 60)), fixture.Directory("B", fixture.Record("same", 60)));
        await Plain();
    }

    [Given("a native directory whose 60-byte A grows to 100 bytes before 60-byte B")]
    public async Task Overflow()
    {
        fixture.Records[fixture.Root.Identity] = new[] { fixture.Record("A", 60), fixture.Record("B", 60, 101) };
        fixture.ReplaceRecord(100, fixture.Directory("replacement"));
        fixture.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(fixture.Record("Z", 100));
        await Plain();
    }

    [When("the descriptor reads at most (.*) bytes")]
    public async Task Read(uint count) => Add(await fixture.Calls.ReadAsync(descriptor, count));

    [When("its duplicate reads at most (.*) bytes")]
    public async Task DuplicateRead(uint count)
    {
        int duplicate = await fixture.Files.Process.Descriptors.DuplicateAsync(descriptor);
        Add(await fixture.Calls.ReadAsync(duplicate, count));
    }

    [When("the native descriptor seeks to absolute zero")]
    public async Task Rewind() => await fixture.Calls.SeekAsync(descriptor, 0, Plan9SeekWhence.Set);

    [Then("the returned native names are (.*)")]
    public void Names(string expected) => Assert.Equal(expected.Split(','), names);

    [Then("provider offsets were (.*)")]
    public void Offsets(string expected) => Assert.Equal(expected.Split(',').Select(ulong.Parse), fixture.Reads.Select(r => r.Offset));

    [Then("returned batch lengths were (.*)")]
    public void Lengths(string expected) => Assert.Equal(expected.Split(',').Select(int.Parse), lengths);

    [Then("two provider handles have been opened")]
    public void TwoOpens() => Assert.Equal(2, fixture.Opens.Count);

    [Then("three provider handles have been opened")]
    public void ThreeOpens() => Assert.Equal(3, fixture.Opens.Count);

    [Then("one member handle has been closed")]
    public void OneClose() => Assert.Single(fixture.Closes);

    [Then("no member handle has been closed")]
    public void NoClose() => Assert.Empty(fixture.Closes);

    [AfterScenario]
    public async Task Cleanup() => await fixture.DisposeAsync();

    private void Add(ReadOnlyMemory<byte> bytes)
    {
        lengths.Add(bytes.Length);
        names.AddRange(DirectoryStreamingTests.Names(bytes));
    }
}

using System.Buffers.Binary;
using System.Reflection;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DirectoryStreamBoundaryTests
{
    [Fact]
    public async Task PublicDescriptorInstallationRetainsItsMetadataLoader()
    {
        await using var f = new FileSyscallFixture();
        var handle = new ResourceOpenHandle(f.Process.Root.Current, "manual", 0, 0);
        int calls = 0;
        int fd = f.Process.Descriptors.Install(
            handle,
            () => ValueTask.CompletedTask,
            readDirectoryAsync: _ =>
            {
                calls++;
                return ValueTask.FromResult<IReadOnlyList<ResourceStat>>(Array.Empty<ResourceStat>());
            });
        Assert.Empty((await f.Calls.ReadAsync(fd, 100)).ToArray());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PlainProviderStreamNeedsNoStatMappingButMountedEntriesDo()
    {
        await using var f = new StreamingDirectoryFixture();
        var calls = new Plan9FileSyscalls(f.Files.Process, f.Files.Plane, f.Files.Context, DirectoryReadMode.ProviderStream);
        int fd = await calls.OpenAsync("/", new(0));
        Assert.Equal(64, (await calls.ReadAsync(fd, 64)).Length);
        f.ReplaceRecord(101, f.Directory("replacement"));
        await Assert.ThrowsAsync<NotSupportedException>(() => calls.ReadAsync(fd, 100).AsTask());
    }

    [Fact]
    public async Task StreamOpenedFromMountedCurrentDirectoryRetainsTheUnionHead()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A", f.Record("a", 60)), f.Directory("B", f.Record("b", 60)));
        f.Files.Process.ChangeDirectory(await f.Files.Local.AttachAsync(f.Root, default));
        int fd = await f.Calls.OpenAsync(".", new(0));
        Assert.Equal(new[] { "a" }, DirectoryStreamingTests.Names(await f.Calls.ReadAsync(fd, 60)));
        Assert.Equal(new[] { "b" }, DirectoryStreamingTests.Names(await f.Calls.ReadAsync(fd, 60)));
    }

    [Fact]
    public async Task OversizedValidReplyIsRejectedBeforeItCanEscapeTheRequestedCount()
    {
        await using var f = new StreamingDirectoryFixture();
        byte[] record = f.Record("valid", 64);
        DirectoryRecords.Validate(record, 64);
        Assert.Throws<IOException>(() => DirectoryRecords.Validate(record, 63));
        f.ReadOverride = (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(record);
        int fd = await f.Calls.OpenAsync("/", new(0));
        await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, 63).AsTask());
    }

    [Theory]
    [InlineData(4091, 1)]
    [InlineData(4092, 2)]
    public async Task StatRetryThresholdIncludesTheOriginalNameLength(int size, int expectedCalls)
    {
        await using var f = new StreamingDirectoryFixture();
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("first", size));
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 8192);
        Assert.Equal(expectedCalls, f.Stats.Count);
    }

    [Theory]
    [InlineData(65537, true)]
    [InlineData(65538, false)]
    public async Task NameReplacementChecksTheFullWireLimit(int replacementSize, bool accepted)
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("first", 64) };
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record(string.Empty, replacementSize - 5));
        int fd = await f.Calls.OpenAsync("/", new(0));
        var bytes = await f.Calls.ReadAsync(fd, 70000);
        Assert.Equal(accepted ? replacementSize : 64, bytes.Length);
        DirectoryRecords.Validate(bytes.Span, 70000);
    }

    [Fact]
    public async Task MalformedReplacementStatCannotReturnInvalidStrings()
    {
        await using var f = new StreamingDirectoryFixture();
        f.ReplaceRecord(100, f.Directory("replacement"));
        byte[] replacement = f.Record("first", 64);
        replacement[^1] = 1;
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(replacement);
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(f.Records[f.Root.Identity][0], (await f.Calls.ReadAsync(fd, 64)).ToArray());
    }

    [Fact]
    public async Task FinalReleaseDiscardsBufferedRecordsEvenWhenAClosedLeaseObjectIsRetained()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("A", 60), f.Record("B", 60, 101) };
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("A", 100));
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 120);
        var lease = f.Files.Process.Descriptors.Acquire(fd);

        // Inspect retained allocation ownership, not the contents or cursor algorithm.
        var rock = (List<byte[]>)typeof(ProviderDirectoryCursor).GetField("rock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease.Directory)!;
        Assert.Single(rock);
        await f.Files.Process.Descriptors.CloseAsync(fd);
        Assert.Single(rock);
        await lease.DisposeAsync();
        Assert.Empty(rock);
    }

    [Fact]
    public async Task MemberOffsetOverflowFailsInsteadOfWrappingToTheBeginning()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A", f.Record("a", 60)), f.Directory("B"));
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 60);
        await using var lease = f.Files.Process.Descriptors.Acquire(fd);
        typeof(ProviderDirectoryCursor).GetField("memberOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lease.Directory, ulong.MaxValue);
        f.ReadOverride = (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("a", 60));
        await Assert.ThrowsAsync<OverflowException>(() => f.Calls.ReadAsync(fd, 60).AsTask());
        Assert.Equal(ulong.MaxValue, f.Reads[^1].Offset);
    }
}

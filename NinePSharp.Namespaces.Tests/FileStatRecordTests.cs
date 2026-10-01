using System.Buffers.Binary;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class FileStatRecordTests
{
    [Theory]
    [InlineData(2, 2, 78)]
    [InlineData(79, 2, 78)]
    [InlineData(80, 2, 90)]
    [InlineData(91, 2, 90)]
    [InlineData(92, 92, 90)]
    [InlineData(uint.MaxValue, 92, 90)]
    public async Task ProviderAndVisibleNamesHaveSeparateSizeHints(uint count, int length, int hint)
    {
        await using var f = new FileStatFixture();
        int fd = f.Files.Process.Descriptors.Install(new(f.Files.Process.Root.Current, "manual", 0, 0),
            () => ValueTask.CompletedTask, visibleName: "abcdefghijklmnop");
        f.Reply = (resource, capacity) =>
        {
            byte[] bytes = f.Record(resource, "name");
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes.Length > capacity ? bytes.AsMemory(0, 2) : bytes);
        };
        var result = await f.Calls.FStatAsync(fd, count);
        Assert.Equal(length, result.Length);
        Assert.Equal(hint, BinaryPrimitives.ReadUInt16LittleEndian(result.Span));
        Assert.Equal(count, Assert.Single(f.Requests).Count);
        if (length > 2) Assert.Equal("abcdefghijklmnop", FileStatSyscallTests.Name(result));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(79)]
    [InlineData(80)]
    [InlineData(uint.MaxValue)]
    public async Task TypedAdapterBoundsPathAndOpenReplies(uint count)
    {
        await using var f = new FileStatFixture();
        var root = f.Files.Process.Root.Current;
        var opened = new ResourceOpenHandle(root, "manual", 0, 0);
        byte[] complete = f.Codec.Encode(await f.Files.Resources.StatAsync(root, default));
        foreach (var result in new[] { await f.Adapter.StatAsync(root, count, default), await f.Adapter.StatAsync(opened, count, default) })
            Assert.Equal(complete.Length > count ? complete[..2] : complete, result.ToArray());
        Assert.Equal(complete, (await f.Adapter.StatAsync(root, (uint)complete.Length, default)).ToArray());
        Assert.Equal(complete, (await f.Adapter.StatAsync(opened, (uint)complete.Length, default)).ToArray());
    }

    [Fact]
    public async Task MinimumRecordIsValidAndAllMetadataStringsSurviveNameChanges()
    {
        await using var f = new FileStatFixture();
        var root = f.Files.Process.Root.Current;
        byte[] minimum = f.Codec.Encode(new(root, "", 0, 0, 0, 0, "", "", ""));
        Assert.Equal(49, minimum.Length);
        Assert.Equal(minimum, FileStatRecords.Rewrite(minimum, 49, "").ToArray());
        Assert.Equal(minimum[..2], FileStatRecords.Rewrite(minimum.AsMemory(0, 2), 2, "").ToArray());
        foreach (string replacement in new[] { "", "x", "same", "longer-name", "é中" })
        {
            var source = new ResourceStat(root, "same", 0x180, 17, 29, 37, "owner-é", "group-中", "modifier");
            Assert.Equal(f.Codec.Encode(source with { Name = replacement }),
                FileStatRecords.Rewrite(f.Codec.Encode(source), 4096, replacement).ToArray());
        }
    }

    [Fact]
    public async Task MalformedProviderRepliesReleaseFstatLeaseAndLeaveDescriptorInstalled()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(0));
        var resource = f.Files.Process.Descriptors.Snapshot()[0].Handle.Resource;
        byte[] valid = f.Record(resource, "provider");
        byte[] badString = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(badString.AsSpan(41), 1000);
        byte[] trailing = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(trailing.AsSpan(41), 0);
        foreach (byte[] bytes in new[] { Array.Empty<byte>(), new byte[1], new byte[2], valid[..79], valid.Concat(valid).ToArray(), badString, trailing })
        {
            f.Reply = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
            await Assert.ThrowsAsync<IOException>(() => f.Calls.FStatAsync(fd, 4096).AsTask());
            Assert.Single(f.Files.Process.Descriptors.Snapshot());
        }
        f.Reply = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(valid);
        await Assert.ThrowsAsync<IOException>(() => f.Calls.FStatAsync(fd, 79).AsTask());
        await f.Files.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(1, f.Files.Resources.ClunkCount);
    }

    [Fact]
    public async Task FullUnsignedPrefixRangeDoesNotWrapAndNameGrowthFailsExplicitly()
    {
        await using var f = new FileStatFixture();
        var resource = f.Files.Process.Root.Current;
        byte[] record = f.Record(resource, "a", 65537);
        Assert.Equal(65537, FileStatRecords.Rewrite(record, 65537, "b").Length);
        Assert.Equal(new byte[] { 255, 255 }, FileStatRecords.Rewrite(record, 65537, "b").Span[..2].ToArray());
        Assert.Throws<IOException>(() => FileStatRecords.Rewrite(record, uint.MaxValue, "bb"));
        Assert.Throws<IOException>(() => FileStatRecords.Rewrite(record, uint.MaxValue, new string('x', 65536)));
        Assert.Equal(record, FileStatRecords.Rewrite(record, uint.MaxValue, null).ToArray());
    }

    [Fact]
    public async Task ReturnedBuffersDoNotAliasProviderMemory()
    {
        await using var f = new FileStatFixture();
        byte[] bytes = f.Record(f.Files.Process.Root.Current, "provider");
        var copy = FileStatRecords.Rewrite(bytes, 100, null);
        var hint = FileStatRecords.Rewrite(bytes.AsMemory(0, 2), 2, "unused");
        bytes[0] = 0;
        Assert.Equal(78, copy.Span[0]);
        Assert.Equal(78, hint.Span[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TypedAdapterRejectsTinyCountsBeforeMetadataWork(uint count)
    {
        await using var f = new FileStatFixture();
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Adapter.StatAsync(f.Files.Process.Root.Current, count, default).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Adapter.StatAsync(new ResourceOpenHandle(f.Files.Process.Root.Current, "manual", 0, 0), count, default).AsTask());
    }

    [Fact]
    public void AdapterRejectsMissingResources()
        => Assert.Throws<ArgumentNullException>(() => new FileStatOperations(null!, Array.Empty<DirectoryDeviceBinding>()));

    [Fact]
    public async Task AdapterDoesNotSubstituteResourceStatForAnUnsupportedOpenHandle()
    {
        await using var f = new FileStatFixture();
        var adapter = new FileStatOperations(new LegacyProvider(), Array.Empty<DirectoryDeviceBinding>());
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.StatAsync(new ResourceOpenHandle(f.Files.Process.Root.Current, "manual", 0, 0), 100, default).AsTask());
        var context = new ResourceOperationContext(new("unsupported-wstat", 1), 1, "user");
        byte[] update = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged());
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.WStatAsync(f.Files.Process.Root.Current, update, context, default).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.WStatAsync(
            new ResourceOpenHandle(f.Files.Process.Root.Current, "manual", 0, 0), update, context, default).AsTask());
    }

    private sealed class LegacyProvider : IResourceDataOperations
    {
        public ValueTask<ResourceStat> StatAsync(ResourceHandle r, CancellationToken t) => throw new InvalidOperationException("resource stat must not be called");
        public ValueTask<ResourceHandle?> WalkAsync(ResourceHandle r, string n, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(ResourceHandle r, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<ResourceHandle> CreateAsync(ResourceHandle r, string n, bool d, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<ResourceOpenHandle> OpenAsync(ResourceHandle r, byte m, ResourceOperationContext c, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(ResourceOpenHandle h, ulong o, uint c, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<uint> WriteAsync(ResourceOpenHandle h, ulong o, ReadOnlyMemory<byte> b, ResourceOperationContext c, CancellationToken t) => throw new NotSupportedException();
        public ValueTask<ResourceOpenHandle> CreateAndOpenAsync(ResourceHandle r, string n, uint p, byte m, ResourceOperationContext c, CancellationToken t) => throw new NotSupportedException();
        public ValueTask ClunkAsync(ResourceOpenHandle h, ResourceOperationContext c, CancellationToken t) => throw new NotSupportedException();
        public ValueTask RemoveAsync(ResourceHandle r, ResourceOpenHandle? h, ResourceOperationContext c, CancellationToken t) => throw new NotSupportedException();
    }
}

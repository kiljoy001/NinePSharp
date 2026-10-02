using System.Buffers.Binary;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DirectoryCursorTests
{
    [Fact]
    public async Task BoundedDirectoryReadsShareCursorThroughDupAndCopyButNotIndependentOpens()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        int duplicate = await f.Process.Descriptors.DuplicateAsync(fd);
        var child = f.Table.Fork(f.Process.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        int independent = await f.Calls.OpenAsync("/", new(0));
        int firstSize = Stat.CalculateSize("file", "owner", "owner", "owner", NinePDialect.NineP2000);
        var first = await f.Calls.ReadAsync(duplicate, (uint)firstSize);
        Assert.Equal(firstSize, first.Length);
        Assert.Equal("file", Assert.Single(Decode(first)).Name);
        Assert.Equal("other", Assert.Single(Decode(await f.ForProcess(child).ReadAsync(fd, 100))).Name);
        Assert.Empty((await f.Calls.ReadAsync(fd, 100)).ToArray());
        Assert.Equal(new[] { "file", "other" }, Decode(await f.Calls.ReadAsync(independent, 1024)).Select(x => x.Name));
    }

    [Fact]
    public async Task PositionedDirectoryReadsAdvanceSharedCursorAndAcceptOnlyZeroOrCurrentOffset()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        var first = await f.Calls.PReadAsync(fd, 0, 68);
        Assert.Equal("file", Assert.Single(Decode(first)).Name);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.PReadAsync(fd, 1, 100).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.PReadAsync(fd, -2, 100).AsTask());
        var second = await f.Calls.PReadAsync(fd, first.Length, 100);
        Assert.Equal("other", Assert.Single(Decode(second)).Name);
        Assert.Empty((await f.Calls.ReadAsync(fd, 100)).ToArray());
        Assert.Equal("file", Assert.Single(Decode(await f.Calls.PReadAsync(fd, 0, 68))).Name);
    }

    [Fact]
    public async Task RewindRefreshesSnapshotAndZeroCountDoesNotLoadProvider()
    {
        await using var f = new FileSyscallFixture();
        int loads = 0;
        f.Plane.DirectoryOverride = async (channel, token) =>
        {
            loads++;
            return await f.Local.ReadDirectoryAsync(channel, token);
        };
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Empty((await f.Calls.ReadAsync(fd, 0)).ToArray());
        Assert.Equal(0, loads);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.ReadAsync(fd, 67).AsTask());
        Assert.Equal("file", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 68))).Name);
        await f.Calls.CreateAsync("/new", new(0x180, 2));
        Assert.Equal(new[] { "other" }, Decode(await f.Calls.ReadAsync(fd, 1024)).Select(x => x.Name));
        Assert.Equal(2, loads);
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(new[] { "file", "other", "new" }, Decode(await f.Calls.ReadAsync(fd, 1024)).Select(x => x.Name));
        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task UnionReadsPreserveDuplicateNamesAndRewindReflectsUnmount()
    {
        await using var f = new FileSyscallFixture();
        var upper = f.Resources.Directory("upper", "same", "upper");
        var lower = f.Resources.Directory("lower", "same", "lower");
        var mounts = f.Process.ProcessGroup.MountTable;
        var root = f.Process.Root.Current;
        mounts.Mount(upper, root);
        mounts.Mount(lower, root, MountFlags.After);
        int fd = await f.Calls.OpenAsync("/", new(0));
        var entries = Decode(await f.Calls.ReadAsync(fd, 4096));
        Assert.Equal(new[] { "same", "upper", "same", "lower" }, entries.Select(x => x.Name));
        Assert.NotEqual(entries[0].Qid.Path, entries[2].Qid.Path);
        mounts.Unmount(root, lower);
        Assert.Empty((await f.Calls.ReadAsync(fd, 4096)).ToArray());
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(new[] { "same", "upper" }, Decode(await f.Calls.ReadAsync(fd, 4096)).Select(x => x.Name));
    }

    [Fact]
    public async Task MountedEntryUsesReplacementMetadataAndPreservesVisibleName()
    {
        await using var f = new FileSyscallFixture();
        var root = f.Process.Root.Current;
        var mountedOn = await f.Resources.WalkAsync(root, "file", default);
        var replacementRoot = f.Resources.Directory("replacement", "different-name");
        var replacement = await f.Resources.WalkAsync(replacementRoot, "different-name", default);
        f.Process.ProcessGroup.MountTable.Mount(NamespaceChannel.Restore(new[] { new ChannelFrame("/", replacement!) }), mountedOn!);
        int fd = await f.Calls.OpenAsync("/", new(0));
        var first = Decode(await f.Calls.ReadAsync(fd, 4096))[0];
        Assert.Equal("file", first.Name);
        Assert.Equal(replacement!.Qid, first.Qid);
    }

    [Fact]
    public async Task NewlyCreatedDirectoryHasAReadableCursor()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.CreateAsync("/dir", new(0x800001ed, 0));
        string[]? visiblePath = null;
        f.Plane.DirectoryOverride = async (channel, token) =>
        {
            visiblePath = channel.VisiblePath.ToArray();
            return await f.Local.ReadDirectoryAsync(channel, token);
        };
        await f.Calls.CreateAsync("/dir/child", new(0x180, 2));
        Assert.Equal("child", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 1024))).Name);
        Assert.Equal(new[] { "dir" }, visiblePath);
    }

    [Fact]
    public async Task RegularFileDescriptorsDoNotOwnDirectoryCursors()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await using DescriptorLease lease = f.Process.Descriptors.Acquire(fd);
        Assert.Throws<NamespaceFidException>(() => lease.Directory);
    }

    [Theory]
    [InlineData(NinePConstants.OWRITE)]
    [InlineData(NinePConstants.ORDWR)]
    public async Task ManuallyInstalledWritableDirectoryCannotReachProviderWrite(byte mode)
    {
        await using var f = new FileSyscallFixture();
        var handle = new ResourceOpenHandle(f.Process.Root.Current, "manual-directory", mode, 0);
        int fd = f.Process.Descriptors.Install(handle, () => ValueTask.CompletedTask);
        bool providerCalled = false;
        f.Plane.WriteOverride = (_, _, data, _, _) =>
        {
            providerCalled = true;
            return ValueTask.FromResult((uint)data.Length);
        };
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WriteAsync(fd, new byte[] { 1 }).AsTask());
        Assert.False(providerCalled);
    }

    [Fact]
    public async Task EmptyDirectoryReadsAtZeroRefreshWhenAnEntryAppears()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.CreateAsync("dir", new(0x800001ed, 0));
        Assert.Empty((await f.Calls.ReadAsync(fd, 1024)).ToArray());
        await f.Calls.CreateAsync("dir/new", new(0x180, 2));
        Assert.Equal("new", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 1024))).Name);
    }

    [Fact]
    public async Task DirectoryStatPreservesUnicodeAndMetadataAndDoesNotRetainMutableProviderList()
    {
        await using var f = new FileSyscallFixture();
        var resource = new ResourceHandle(new ResourceIdentity("provider", "device", 12345), QidType.QTFILE, 42);
        var first = new ResourceStat(resource, "é星", 0x180, 123, 456, 789, "用户", "group", "modifier");
        var entries = new List<ResourceStat> { first, first with { Name = "second" } };
        f.Plane.DirectoryOverride = (_, _) => ValueTask.FromResult<IReadOnlyList<ResourceStat>>(entries);
        int fd = await f.Calls.OpenAsync("/", new(0));
        uint size = Stat.CalculateSize(first.Name, first.User, first.Group, first.LastModifier, NinePDialect.NineP2000);
        var stat = Assert.Single(Decode(await f.Calls.ReadAsync(fd, size)));
        Assert.Equal(first.Resource.Qid, stat.Qid);
        Assert.Equal(first.Name, stat.Name);
        Assert.Equal(first.Mode, stat.Mode);
        Assert.Equal(first.AccessTime, stat.Atime);
        Assert.Equal(first.ModificationTime, stat.Mtime);
        Assert.Equal(first.Length, stat.Length);
        Assert.Equal(first.User, stat.Uid);
        Assert.Equal(first.Group, stat.Gid);
        Assert.Equal(first.LastModifier, stat.Muid);
        entries.Clear();
        Assert.Equal("second", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 1024))).Name);
    }

    [Fact]
    public async Task CancelledListingReleasesCursorGateAndManualDirectoryDescriptorNeedsALoader()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        f.Plane.DirectoryOverride = (_, _) => throw new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Calls.ReadAsync(fd, 100).AsTask());
        f.Plane.DirectoryOverride = null;
        Assert.Equal("file", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 68))).Name);
        var handle = f.Process.Descriptors.Snapshot()[fd].Handle;
        int unconfigured = f.Process.Descriptors.Install(handle, () => ValueTask.CompletedTask);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.ReadAsync(unconfigured, 100).AsTask());
    }

    [Fact]
    public async Task DirectoryLoaderRetainsProviderVersionReturnedByOpen()
    {
        await using var f = new FileSyscallFixture();
        f.Resources.AdvanceVersionOnOpen = true;
        uint version = 0;
        f.Plane.DirectoryOverride = (channel, _) =>
        {
            version = channel.Current.Version;
            return ValueTask.FromResult<IReadOnlyList<ResourceStat>>(Array.Empty<ResourceStat>());
        };
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 100);
        Assert.Equal(1U, version);
    }

    [Fact]
    public async Task PendingDirectoryReadPinsHandleAcrossCloseReuseAndExit()
    {
        await using var f = new FileSyscallFixture();
        var pending = new TaskCompletionSource<IReadOnlyList<ResourceStat>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listing = await f.Local.ReadDirectoryAsync(f.Process.Root, default);
        f.Plane.DirectoryOverride = (_, _) => new(pending.Task);
        int fd = await f.Calls.OpenAsync("/", new(0));
        var reading = f.Calls.ReadAsync(fd, 4096).AsTask();
        await f.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(0, f.Resources.ClunkCount);
        Assert.Equal(fd, await f.OpenAsync());
        await f.Table.TerminateAsync(f.Process.Id);
        Assert.Equal(1, f.Resources.ClunkCount);
        pending.SetResult(listing);
        Assert.Equal(new[] { "file", "other" }, Decode(await reading).Select(x => x.Name));
        Assert.Equal(2, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task ConcurrentReadsSerializeAndCancelledWaiterDoesNotAdvanceCursor()
    {
        await using var f = new FileSyscallFixture();
        var pending = new TaskCompletionSource<IReadOnlyList<ResourceStat>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listing = await f.Local.ReadDirectoryAsync(f.Process.Root, default);
        int loads = 0;
        f.Plane.DirectoryOverride = (_, _) =>
        {
            loads++;
            return new(pending.Task);
        };
        int fd = await f.Calls.OpenAsync("/", new(0));
        var first = f.Calls.ReadAsync(fd, 68).AsTask();
        using var cancellation = new CancellationTokenSource();
        var cancelled = f.Calls.ReadAsync(fd, 100, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var second = f.Calls.ReadAsync(fd, 100).AsTask();
        Assert.False(second.IsCompleted);
        Assert.Equal(1, loads);
        pending.SetResult(listing);
        Assert.Equal("file", Assert.Single(Decode(await first)).Name);
        Assert.Equal("other", Assert.Single(Decode(await second)).Name);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task FailedListingCanBeRetriedAndRewindWaitsForPendingRead()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        f.Plane.DirectoryOverride = (_, _) => throw new IOException("listing failed");
        await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, 100).AsTask());
        var pending = new TaskCompletionSource<IReadOnlyList<ResourceStat>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plane.DirectoryOverride = (_, _) => new(pending.Task);
        var reading = f.Calls.ReadAsync(fd, 68).AsTask();
        var rewind = f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set).AsTask();
        Assert.False(rewind.IsCompleted);
        pending.SetResult(await f.Local.ReadDirectoryAsync(f.Process.Root, default));
        await reading;
        Assert.Equal(0, await rewind);
        Assert.Equal("file", Assert.Single(Decode(await f.Calls.ReadAsync(fd, 68))).Name);
    }

    [Theory]
    [InlineData(65486, true)]
    [InlineData(65487, false)]
    public async Task OversizedStatIsRejectedWithoutWrappedLength(int nameSize, bool accepted)
    {
        await using var f = new FileSyscallFixture();
        var entry = new ResourceStat(f.Process.Root.Current, new string('x', nameSize), 0, 0, 0, 0, string.Empty, string.Empty, string.Empty);
        f.Plane.DirectoryOverride = (_, _) => ValueTask.FromResult<IReadOnlyList<ResourceStat>>(new[] { entry });
        int fd = await f.Calls.OpenAsync("/", new(0));
        if (accepted)
        {
            Assert.Equal(ushort.MaxValue, (await f.Calls.ReadAsync(fd, ushort.MaxValue)).Length);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, uint.MaxValue).AsTask());
        }
    }

    [Property(MaxTest = 100)]
    public async Task GeneratedReadBudgetsNeverSplitOrLoseDirectoryRecords(NonEmptyArray<byte> input)
    {
        await using var f = new FileSyscallFixture();
        var names = input.Get.Take(32).Select((b, i) => "entry-" + i + new string('x', b % 16)).ToArray();
        var root = f.Resources.Directory("generated", names);
        f.Process.ChangeDirectory(await f.Plane.AttachAsync(root, default));
        int fd = await f.Calls.OpenAsync(".", new(0));
        var actual = new List<string>();
        foreach (byte value in input.Get.Take(32).Append((byte)255))
        {
            uint budget = (uint)(90 + value);
            ReadOnlyMemory<byte> bytes = await f.Calls.ReadAsync(fd, budget);
            Assert.True(bytes.Length <= budget);
            actual.AddRange(Decode(bytes).Select(x => x.Name));
        }

        actual.AddRange(Decode(await f.Calls.ReadAsync(fd, 65535)).Select(x => x.Name));
        Assert.Equal(names, actual);
    }

    internal static Stat[] Decode(ReadOnlyMemory<byte> bytes)
    {
        var result = new List<Stat>();
        int offset = 0;
        while (offset < bytes.Length)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[offset..]) + 2;
            int end = offset + size;
            Assert.InRange(end, offset + 49, bytes.Length);
            int consumed = 0;
            var stat = new Stat(bytes.Span.Slice(offset, size), ref consumed);
            Assert.Equal(size, consumed);
            result.Add(stat);
            offset = end;
        }

        return result.ToArray();
    }
}

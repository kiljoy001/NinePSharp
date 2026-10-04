using System.Buffers.Binary;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DirectoryStreamingTests
{
    [Fact]
    public async Task ProviderOffsetsShortReadsDupCopyAndIndependentOpens()
    {
        await using var f = new StreamingDirectoryFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        int dup = await f.Files.Process.Descriptors.DuplicateAsync(fd);
        var child = f.Files.Table.Fork(f.Files.Process.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        int other = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(64, (await f.Calls.ReadAsync(dup, 80)).Length);
        Assert.Equal(72, (await f.ForProcess(child).PReadAsync(fd, 64, 80)).Length);
        Assert.Empty((await f.Calls.ReadAsync(fd, 80)).ToArray());
        Assert.Equal(64, (await f.Calls.ReadAsync(other, 80)).Length);
        Assert.Equal(new ulong[] { 0, 64, 136, 0 }, f.Reads.Select(r => r.Offset));
        Assert.Equal(f.Reads[0].Handle, f.Reads[1].Handle);
        Assert.NotEqual(f.Reads[0].Handle, f.Reads[3].Handle);
        await f.Files.Process.Descriptors.CloseAsync(dup);
        Assert.Empty(f.Closes);
    }

    [Fact]
    public async Task PositionedReadsSentinelZeroCountRewindAndDefiniteError()
    {
        await using var f = new StreamingDirectoryFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        await Assert.ThrowsAsync<ResourceDirectoryRejectedException>(() => f.Calls.ReadAsync(fd, 63).AsTask());
        Assert.Equal(64, (await f.Calls.PReadAsync(fd, -1, 64)).Length);
        foreach ((long invalid, string error) in new[] { (-2L, "negative offset"), (1L, "invalid directory offset") })
        {
            Assert.Equal(error, (await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.PReadAsync(fd, invalid, 100).AsTask())).Message);
        }

        Assert.Equal(2, f.Reads.Count);
        Assert.Equal(72, (await f.Calls.ReadAsync(fd, 72)).Length);
        Assert.Empty((await f.Calls.PReadAsync(fd, 0, 0)).ToArray());
        Assert.Equal((0UL, 0U), (f.Reads[^1].Offset, f.Reads[^1].Count));
        Assert.Equal(64, (await f.Calls.ReadAsync(fd, 64)).Length);
        foreach (var seek in new[] { (1L, Plan9SeekWhence.Set), (-1L, Plan9SeekWhence.Set), (0L, Plan9SeekWhence.Current), (0L, Plan9SeekWhence.End) })
        {
            await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.SeekAsync(fd, seek.Item1, seek.Item2).AsTask());
        }

        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(64, (await f.Calls.ReadAsync(fd, 64)).Length);
        Assert.Equal(0UL, f.Reads[^1].Offset);
    }

    [Fact]
    public async Task LazyUnionMembersSkipDefiniteFailuresAndRewindDefersClunk()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("same", 60), f.Record("a", 60));
        var b = f.Directory("B", f.Record("same", 60));
        f.Union(a, b);
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Single(f.Opens);
        Assert.Equal(a, f.Opens[0].Resource);
        Assert.Equal(new[] { "same" }, Names(await f.Calls.ReadAsync(fd, 60)));
        Assert.Equal(2, f.Opens.Count);
        Assert.NotEqual(f.Opens[0].HandleId, f.Opens[1].HandleId);
        Assert.Equal(new[] { "a" }, Names(await f.Calls.PReadAsync(fd, 999, 60)));
        Assert.Equal(new[] { "same" }, Names(await f.Calls.ReadAsync(fd, 60)));
        Assert.Equal(a, Assert.Single(f.Closes).Resource);
        Assert.Equal(3, f.Opens.Count);
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Single(f.Closes);
        await f.Calls.ReadAsync(fd, 60);
        Assert.Equal(b, f.Closes[1].Resource);
        Assert.Equal(a, f.Opens[^1].Resource);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnionOpenOrReadRejectionSkipsMember(bool failOpen)
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a", 60));
        var b = f.Directory("B", f.Record("b", 60));
        f.Union(a, b);
        int fd = await f.Calls.OpenAsync("/", new(0));
        if (failOpen)
        {
            f.BeforeOpen = resource => resource == a
            ? throw new ResourceDirectoryRejectedException("open rejected") : ValueTask.CompletedTask;
        }
        else
        {
            f.ReadOverride = (handle, offset, count) => handle.Resource == a
            ? throw new ResourceDirectoryRejectedException("read rejected") : ValueTask.FromResult(f.ReadRecords(handle.Resource, offset, count));
        }

        Assert.Equal(new[] { "b" }, Names(await f.Calls.ReadAsync(fd, 100)));
        Assert.Equal(failOpen ? 0 : 1, f.Closes.Count);
        Assert.Empty((await f.Calls.ReadAsync(fd, 100)).ToArray());
        Assert.Equal(b, f.Closes[^1].Resource);
    }

    [Fact]
    public async Task InitialOpenFailureDoesNotTryAnotherMemberAndUnknownOpenIsNotSkipped()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A");
        var b = f.Directory("B", f.Record("b", 60));
        f.Union(a, b);
        f.BeforeOpen = _ => throw new ResourceDirectoryRejectedException("initial rejected");
        await Assert.ThrowsAsync<ResourceDirectoryRejectedException>(() => f.Calls.OpenAsync("/", new(0)).AsTask());
        Assert.Empty(f.Files.Process.Descriptors.Snapshot());
        f.BeforeOpen = null;
        int fd = await f.Calls.OpenAsync("/", new(0));
        f.BeforeOpen = _ => throw new IOException("lost reply");
        var unknown = await Assert.ThrowsAsync<DirectoryOperationUncertainException>(() => f.Calls.ReadAsync(fd, 100).AsTask());
        Assert.True(unknown.Context.OperationId.Sequence > 0);
        f.BeforeOpen = null;
        await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, 100).AsTask());
        Assert.Single(f.Opens);
    }

    [Fact]
    public async Task SingleMemberOpenStaysIndependentWhileRetainedUnionSeesAppendAfterEof()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a", 60));
        var b = f.Directory("B", f.Record("b", 60));
        var c = f.Directory("C", f.Record("c", 60));
        f.Union(a);
        int single = await f.Calls.OpenAsync("/", new(0));
        f.Mounts.Mount(b, f.Root, MountFlags.After);
        Assert.Equal(new[] { "a" }, Names(await f.Calls.ReadAsync(single, 100)));
        Assert.Empty((await f.Calls.ReadAsync(single, 100)).ToArray());
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 100);
        await f.Calls.ReadAsync(fd, 100);
        Assert.Empty((await f.Calls.ReadAsync(fd, 100)).ToArray());
        f.Mounts.Mount(c, f.Root, MountFlags.After);
        Assert.Equal(new[] { "c" }, Names(await f.Calls.ReadAsync(fd, 100)));
    }

    [Fact]
    public async Task SelectedUnmountAndPrependPreserveNativePositionalTraversal()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a1", 60), f.Record("a2", 60));
        var b = f.Directory("B", f.Record("b", 60));
        var c = f.Directory("C", f.Record("c", 60));
        f.Union(a, b, c);
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 60);
        f.Mounts.Unmount(f.Root, a);
        Assert.Equal(new[] { "a2" }, Names(await f.Calls.ReadAsync(fd, 60)));
        Assert.Equal(new[] { "c" }, Names(await f.Calls.ReadAsync(fd, 60)));
        f.Union(a, b);
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        await f.Calls.ReadAsync(fd, 120);
        var x = f.Directory("X", f.Record("x", 60));
        f.Mounts.Mount(x, f.Root, MountFlags.Before);
        Assert.Equal(new[] { "a1" }, Names(await f.Calls.ReadAsync(fd, 60)));
    }

    [Fact]
    public async Task CompleteUnmountDoesNotRetargetOldHeadButReplacementReusesIt()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a", 60));
        var b = f.Directory("B", f.Record("b", 60));
        var c = f.Directory("C", f.Record("c", 60));
        f.Union(a, b);
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 60);
        await f.Calls.ReadAsync(fd, 60);
        f.Mounts.Mount(c, f.Root);
        Assert.Empty((await f.Calls.ReadAsync(fd, 60)).ToArray());
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(new[] { "c" }, Names(await f.Calls.ReadAsync(fd, 60)));
        f.Mounts.Unmount(f.Root);
        f.Mounts.Mount(a, f.Root);
        Assert.Empty((await f.Calls.ReadAsync(fd, 60)).ToArray());
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Empty((await f.Calls.ReadAsync(fd, 60)).ToArray());
        int fresh = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(new[] { "a" }, Names(await f.Calls.ReadAsync(fresh, 60)));
    }

    [Fact]
    public async Task MountfixPreservesNameBuffersTailsAndCountsRockBytesAgain()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("A", 60), f.Record("B", 60, 101) };
        var target = f.Directory("replacement");
        f.ReplaceRecord(100, target);
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 100));
        int fd = await f.Calls.OpenAsync("/", new(0));
        var first = await f.Calls.ReadAsync(fd, 120);
        Assert.Equal(100, first.Length);
        Assert.Equal(new[] { "A" }, Names(first));
        Assert.Equal(new[] { "B" }, Names(await f.Calls.PReadAsync(fd, 999, 120)));
        Assert.Single(f.Reads);
        f.ReadOverride = (_, offset, _) =>
        {
            Assert.Equal(180UL, offset);
            return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        };
        await f.Calls.PReadAsync(fd, 160, 120);
    }

    [Fact]
    public async Task MountrockEvictsMultipleTailsInReverseOrderAndRewritesThemAtDrainTime()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("A", 60), f.Record("B", 60, 101), f.Record("C", 60, 102) };
        var target = f.Directory("replacement");
        f.ReplaceRecord(100, target);
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 150));
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(150, (await f.Calls.ReadAsync(fd, 180)).Length);
        f.ReplaceRecord(102, target);
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 65));
        var rest = await f.Calls.ReadAsync(fd, 180);
        Assert.Equal(new[] { "C", "B" }, Names(rest));
        Assert.Equal(125, rest.Length);
        Assert.Single(f.Reads);
    }

    [Fact]
    public async Task OversizedReplacementCanReturnZeroThenImplicitZeroRewinds()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("A", 60) };
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 100));
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Empty((await f.Calls.ReadAsync(fd, 80)).ToArray());
        Assert.Equal(100, (await f.Calls.ReadAsync(fd, 100)).Length);
        Assert.Equal(new ulong[] { 0, 0 }, f.Reads.Select(r => r.Offset));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailedOrShortReplacementStatReturnsOriginal(int failure)
    {
        await using var f = new StreamingDirectoryFixture();
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => failure switch
        {
            0 => throw new IOException("stat failed"),
            1 => ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
            _ => ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 60, 0 }),
        };
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(f.Records[f.Root.Identity][0], (await f.Calls.ReadAsync(fd, 64)).ToArray());
    }

    [Fact]
    public async Task LargeStatRetriesAndRetainedOriginalInUnionSuppressesRewrite()
    {
        await using var f = new StreamingDirectoryFixture();
        var target = f.Directory("replacement");
        f.ReplaceRecord(100, target);
        byte[] large = f.Record("different", 5000);
        f.StatOverride = (_, count) => ValueTask.FromResult<ReadOnlyMemory<byte>>(count < large.Length ? large.AsMemory(0, 2) : large);
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(4996 + 72, (await f.Calls.ReadAsync(fd, 6000)).Length);
        Assert.Equal(new uint[] { 4096, 5005 }, f.Stats.Select(s => s.Count));
        var original = new ResourceHandle(new("memory-data", "root", 100), target.Type);
        f.Mounts.Mount(original, original, MountFlags.After);
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(64, (await f.Calls.ReadAsync(fd, 64)).Length);
        Assert.Equal(2, f.Stats.Count);
    }

    [Fact]
    public async Task MountfixUsesCallingNamespaceOfSharedDescriptor()
    {
        await using var f = new StreamingDirectoryFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        var child = f.Files.Table.Fork(f.Files.Process.Id, NamespaceForkMode.Copy, descriptorMode: DescriptorForkMode.Share);
        var target = f.Directory("replacement");
        child.ProcessGroup.MountTable.Mount(
            NamespaceChannel.Restore(new[] { new ChannelFrame("/", target) }),
            new ResourceHandle(new("memory-data", "root", 100), target.Type));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("first", 80));
        Assert.Equal(80, (await f.ForProcess(child).ReadAsync(fd, 100)).Length);
        Assert.Equal(72, (await f.Calls.ReadAsync(fd, 100)).Length);
    }

    [Fact]
    public async Task PendingReadSerializesMutationAndRewindAndSurvivesCloseReuseExit()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a", 60));
        var b = f.Directory("B", f.Record("b", 60));
        f.Union(a, b);
        int fd = await f.Calls.OpenAsync("/", new(0));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadOverride = async (handle, offset, count) =>
        {
            entered.TrySetResult();
            await release.Task;
            return f.ReadRecords(handle.Resource, offset, count);
        };
        Task<ReadOnlyMemory<byte>> reading = f.Calls.ReadAsync(fd, 60).AsTask();
        await entered.Task;
        Task mutation = f.Mounts.UnmountAsync(f.Root, b).AsTask();
        Assert.False(mutation.IsCompleted);
        Assert.Throws<NamespaceMutationBusyException>(() => f.Mounts.Unmount(f.Root));
        using var cancellation = new CancellationTokenSource();
        var cancelled = f.Calls.ReadAsync(fd, 60, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var rewind = f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set).AsTask();
        Assert.False(rewind.IsCompleted);
        await f.Files.Process.Descriptors.CloseAsync(fd);
        Assert.Empty(f.Closes);
        Assert.Equal(fd, await f.Calls.OpenAsync("/", new(0)));
        release.SetResult();
        Assert.Equal(new[] { "a" }, Names(await reading));
        await mutation;
        await rewind;
        Assert.Equal(2, f.Closes.Count);
        await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        Assert.Equal(3, f.Closes.Count);
    }

    [Fact]
    public async Task LateMemberOpenAfterExitClosesBothOwnedHandles()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A", f.Record("a", 60)), f.Directory("B"));
        int fd = await f.Calls.OpenAsync("/", new(0));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeOpen = _ => new ValueTask(release.Task);
        var reading = f.Calls.ReadAsync(fd, 60).AsTask();
        await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        Assert.Empty(f.Closes);
        release.SetResult();
        Assert.Equal(new[] { "a" }, Names(await reading));
        Assert.Equal(2, f.Closes.Count);
        Assert.Equal(2, f.Closes.Select(c => c.HandleId).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task InvalidProviderBytesPoisonCursorWithoutUnsafeReplay(int defect)
    {
        await using var f = new StreamingDirectoryFixture();
        byte[] data = f.Record("record", 64);
        switch (defect)
        {
            case 0: data = new byte[] { 0 }; break;
            case 1: BinaryPrimitives.WriteUInt16LittleEndian(data, 99); break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(41), 99); break;
            case 3: data = new byte[1]; break;
            case 4: data[^1] = 1; break;
        }

        f.ReadOverride = (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(data);
        int fd = await f.Calls.OpenAsync("/", new(0));
        await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, defect == 3 ? 0U : 64U).AsTask());
        f.ReadOverride = null;
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        await Assert.ThrowsAsync<IOException>(() => f.Calls.ReadAsync(fd, 64).AsTask());
        Assert.Single(f.Reads);
    }

    [Fact]
    public async Task SmallOverflowBufferFallsThroughButDoesNotDiscardBufferedRecord()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Records[f.Root.Identity] = new[] { f.Record("A", 60), f.Record("B", 100, 101) };
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 130));
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(130, (await f.Calls.ReadAsync(fd, 160)).Length);
        Assert.Empty((await f.Calls.ReadAsync(fd, 80)).ToArray());
        Assert.Equal(160UL, f.Reads[^1].Offset);
        Assert.Equal(new[] { "B" }, Names(await f.Calls.ReadAsync(fd, 100)));
        Assert.Equal(2, f.Reads.Count);
    }

    [Fact]
    public async Task BufferedUnionRecordsSurviveUnmountAndCloseFailuresStillReleaseMember()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("A", 60), f.Record("B", 60, 101));
        f.Union(a, f.Directory("B"));
        f.ReplaceRecord(100, f.Directory("replacement"));
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Record("Z", 100));
        int fd = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(100, (await f.Calls.ReadAsync(fd, 120)).Length);
        f.Mounts.Unmount(f.Root);
        Assert.Equal(new[] { "B" }, Names(await f.Calls.ReadAsync(fd, 120)));
        Assert.Single(f.Reads);
        Assert.Empty((await f.Calls.ReadAsync(fd, 120)).ToArray());
        Assert.Empty(f.Closes);
        f.Files.Resources.FailClunk = true;
        await f.Files.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(2, f.Closes.Count);
    }

    [Fact]
    public async Task ZeroCountUnionReadCanExhaustEveryMemberAndRewindRecovers()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A", f.Record("a", 60)), f.Directory("B", f.Record("b", 60)));
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 60);
        Assert.Empty((await f.Calls.ReadAsync(fd, 0)).ToArray());
        Assert.Equal(2, f.Closes.Count);
        Assert.Empty((await f.Calls.ReadAsync(fd, 60)).ToArray());
        Assert.Equal(new[] { "a" }, Names(await f.Calls.PReadAsync(fd, 0, 60)));
    }

    [Fact]
    public async Task ReplacementUsesIdentityRatherThanNameAndRetainsAllReplacementFields()
    {
        await using var f = new StreamingDirectoryFixture();
        var target = f.Directory("replacement");
        f.ReplaceRecord(100, target);
        byte[] original = f.Record("é星", 64);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(9), 999);
        f.Records[f.Root.Identity] = new[] { original, f.Record("é星", 64, 101) };
        var stat = new ResourceStat(target with { Version = 17 }, "different", 0x180, 123, 456, 789, "owner", "group", "modifier");
        f.StatOverride = (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(f.Codec.Encode(stat));
        int fd = await f.Calls.OpenAsync("/", new(0));
        var records = DirectoryCursorTests.Decode(await f.Calls.ReadAsync(fd, 512));
        Assert.Equal(new[] { "é星", "é星" }, records.Select(r => r.Name));
        Assert.Equal(stat.Resource.Qid, records[0].Qid);
        Assert.Equal(5U, records[0].Dev);
        Assert.Equal((ushort)7, records[0].Type);
        Assert.Equal(stat.Mode, records[0].Mode);
        Assert.Equal(stat.Length, records[0].Length);
        Assert.Equal(stat.AccessTime, records[0].Atime);
        Assert.Equal(stat.ModificationTime, records[0].Mtime);
        Assert.Equal(stat.User, records[0].Uid);
        Assert.Equal(stat.Group, records[0].Gid);
        Assert.Equal(stat.LastModifier, records[0].Muid);
        Assert.Equal(101UL, records[1].Qid.Path);
        Assert.Single(f.Stats);
    }

    [Fact]
    public async Task AsyncHeadMutationsAreCancellableAndDoNotBlockOtherHeads()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A", f.Record("a", 60));
        var b = f.Directory("B");
        f.Union(a, b);
        int fd = await f.Calls.OpenAsync("/", new(0));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadOverride = async (handle, offset, count) =>
        {
            await release.Task;
            return f.ReadRecords(handle.Resource, offset, count);
        };
        var reading = f.Calls.ReadAsync(fd, 60).AsTask();
        using var cancellation = new CancellationTokenSource();
        var mutation = f.Mounts.MountAsync(b, f.Root, cancellationToken: cancellation.Token).AsTask();
        Assert.False(mutation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation);
        var x = f.Directory("X");
        var c = f.Directory("C");
        await f.Mounts.MountAsync(c, x);
        Assert.Equal(c, f.Mounts.Find(x.Identity)!.Mounts[0].Target);
        release.SetResult();
        await reading;
        await f.Mounts.MountAsync(NamespaceChannel.Restore(new[] { new ChannelFrame("/", b) }), f.Root);
        Assert.Equal(b, f.Mounts.Find(f.Root.Identity)!.Mounts[0].Target);
    }

    [Property(MaxTest = 100)]
    public async Task GeneratedProviderBatchesPreserveWholeRecordsAndOffsets(NonEmptyArray<byte> input)
    {
        await using var f = new StreamingDirectoryFixture();
        var records = input.Get.Take(20).Select((b, i) => f.Record("n" + i, 64 + (b % 32), (ulong)(100 + i))).ToArray();
        f.Records[f.Root.Identity] = records;
        int fd = await f.Calls.OpenAsync("/", new(0));
        var names = new List<string>();
        for (int i = 0; i <= records.Length; i++)
        {
            names.AddRange(Names(await f.Calls.ReadAsync(fd, (uint)(96 + input.Get[i % input.Get.Length]))));
        }

        Assert.Equal(records.Select(r => Names(r).Single()), names);
        ulong expected = 0;
        foreach (var read in f.Reads)
        {
            Assert.Equal(expected, read.Offset);
            expected += (uint)f.ReadRecords(read.Handle.Resource, read.Offset, read.Count).Length;
        }
    }

    internal static string[] Names(ReadOnlyMemory<byte> bytes) => DirectoryCursorTests.Decode(bytes).Select(s => s.Name!).ToArray();
}

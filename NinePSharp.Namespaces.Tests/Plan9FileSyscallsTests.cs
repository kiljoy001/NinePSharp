using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class Plan9FileSyscallsTests
{
    [Fact]
    public async Task DependenciesPathsAndOpenContextFailuresAreRejectedBeforeOwnershipTransfer()
    {
        await using var f = new FileSyscallFixture();
        Assert.Throws<ArgumentNullException>(() => new Plan9FileSyscalls(null!, f.Plane, f.Context));
        Assert.Throws<ArgumentNullException>(() => new Plan9FileSyscalls(f.Process, null!, f.Context));
        Assert.Throws<ArgumentNullException>(() => new Plan9FileSyscalls(f.Process, f.Plane, null!));
        Assert.Throws<ArgumentNullException>(() => f.Local.StatAsync((ResourceOpenHandle)null!, default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Calls.OpenAsync(" ", new(0)).AsTask());
        int contexts = 0;
        var calls = new Plan9FileSyscalls(f.Process, f.Plane, () => ++contexts == 2 ? throw new IOException("identity unavailable") : f.Context());
        int opens = 0;
        f.Plane.AfterOpen = _ => { opens++; return Task.CompletedTask; };
        await Assert.ThrowsAsync<IOException>(() => calls.OpenAsync("/file", new(0)).AsTask());
        Assert.Equal(0, opens);
        Assert.Empty(f.Process.Descriptors.Snapshot());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.OpenAsync("/file", new(0), new CancellationToken(true)).AsTask());
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task RelativeSeeksAreAtomicAcrossDuplicatedDescriptors()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        int duplicate = await f.Process.Descriptors.DuplicateAsync(fd);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
            await f.Calls.SeekAsync(i % 2 == 0 ? fd : duplicate, 1, Plan9SeekWhence.Current))));
        Assert.Equal(100, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
    }

    [Fact]
    public async Task ProviderAcknowledgedCancellationCorrectsReservationAndReleasesLease()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        var completion = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plane.WriteOverride = (_, _, _, _, _) => new(completion.Task);
        Task<uint> write = f.Calls.WriteAsync(fd, new byte[4]).AsTask();
        await f.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(0, f.Resources.ClunkCount);
        completion.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Equal(1, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task OpenUsesPathsLowestSlotIndependentPositionAndDescriptorLocalExecFlag()
    {
        await using var f = new FileSyscallFixture();
        int first = await f.OpenAsync(NinePConstants.ORDWR | NinePConstants.OCEXEC | NinePConstants.ORCLOSE);
        Assert.Equal(0, first);
        DescriptorSlot slot = Assert.Single(f.Process.Descriptors.Snapshot());
        Assert.True(slot.CloseOnExec);
        Assert.Equal(NinePConstants.ORDWR | NinePConstants.ORCLOSE, slot.Handle.Mode);
        Assert.Equal(3U, await f.Calls.WriteAsync(first, "abc"u8.ToArray()));
        int second = await f.Calls.OpenAsync("./file", new(NinePConstants.ORDWR));
        Assert.Equal(1, second);
        Assert.False(f.Process.Descriptors.Snapshot()[1].CloseOnExec);
        Assert.Equal("abc"u8.ToArray(), (await f.Calls.ReadAsync(second, 10)).ToArray());
        Assert.Empty((await f.Calls.ReadAsync(first, 10)).ToArray());
        await f.Process.Descriptors.CloseOnExecAsync();
        Assert.Equal(1, f.Resources.ClunkCount);
        Assert.Equal(0, await f.OpenAsync());
    }

    [Fact]
    public async Task OpenFollowsRelativeUnionFallback()
    {
        await using var f = new FileSyscallFixture();
        ResourceHandle upper = f.Resources.Directory("upper", "only-upper");
        ResourceHandle lower = f.Resources.Directory("lower", "only-lower");
        ResourceHandle root = f.Process.Root.Current;
        f.Process.ProcessGroup.MountTable.Mount(upper, root);
        f.Process.ProcessGroup.MountTable.Mount(lower, root, MountFlags.After);
        var atUnion = await f.Plane.AttachAsync(root, default);
        f.Process.ChangeDirectory(atUnion);
        int fd = await f.Calls.OpenAsync("only-lower", new(0));
        Assert.Equal("lower", Assert.Single(f.Process.Descriptors.Snapshot()).Handle.Resource.Identity.Device);
        Assert.Equal(0, fd);
    }

    [Fact]
    public async Task AbsoluteOpenStartsAtRootEvenAfterChangingCurrentDirectory()
    {
        await using var f = new FileSyscallFixture();
        var elsewhere = f.Resources.Directory("elsewhere", "file");
        f.Process.ChangeDirectory(await f.Plane.AttachAsync(elsewhere, default));
        int absolute = await f.OpenAsync();
        int relative = await f.Calls.OpenAsync("file", new(2));
        Assert.Equal("root", f.Process.Descriptors.Snapshot()[absolute].Handle.Resource.Identity.Device);
        Assert.Equal("elsewhere", f.Process.Descriptors.Snapshot()[relative].Handle.Resource.Identity.Device);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    public async Task AccessModesAreEnforced(byte mode, bool readable, bool writable)
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync(mode);
        if (readable) Assert.Empty((await f.Calls.ReadAsync(fd, 1)).ToArray());
        else await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.ReadAsync(fd, 1).AsTask());
        if (writable) Assert.Equal(1U, await f.Calls.WriteAsync(fd, new byte[1]));
        else await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WriteAsync(fd, new byte[1]).AsTask());
    }

    [Fact]
    public async Task DupAndCopiedTablesShareOffsetsAndPositionedIoDoesNotMoveThem()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.WriteAsync(fd, "abcdef"u8.ToArray());
        int dup = await f.Process.Descriptors.DuplicateAsync(fd);
        VProcess child = f.Table.Fork(f.Process.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        var childCalls = f.ForProcess(child);
        Assert.Equal(0, await f.Calls.SeekAsync(dup, 0, Plan9SeekWhence.Set));
        Assert.Equal("ab"u8.ToArray(), (await childCalls.ReadAsync(fd, 2)).ToArray());
        Assert.Equal("cd"u8.ToArray(), (await f.Calls.PReadAsync(dup, -1, 2)).ToArray());
        Assert.Equal("bc"u8.ToArray(), (await f.Calls.PReadAsync(fd, 1, 2)).ToArray());
        Assert.Equal(1U, await childCalls.PWriteAsync(fd, 0, "X"u8.ToArray()));
        Assert.Equal(4, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        Assert.Equal("ef"u8.ToArray(), (await f.Calls.ReadAsync(fd, 100)).ToArray());
        Assert.Empty((await childCalls.ReadAsync(fd, 1)).ToArray());
        Assert.Equal(6, await f.Calls.SeekAsync(dup, 0, Plan9SeekWhence.Current));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingWritesReserveRangesThenCorrectShortOrFailedCompletion(bool fail)
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        var pending = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offsets = new List<ulong>();
        f.Plane.WriteOverride = (_, offset, data, _, _) =>
        {
            offsets.Add(offset);
            return offset == 0 ? new(pending.Task) : ValueTask.FromResult((uint)data.Length);
        };
        Task<uint> first = f.Calls.WriteAsync(fd, new byte[8]).AsTask();
        try
        {
            Assert.False(first.IsCompleted);
            Assert.Equal(4U, await f.Calls.WriteAsync(fd, new byte[4]));
            Assert.Equal(new ulong[] { 0, 8 }, offsets);
            Assert.Equal(12, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        }
        finally
        {
            if (fail) pending.SetException(new IOException("failed"));
            else pending.SetResult(3);
        }
        if (fail) await Assert.ThrowsAsync<IOException>(() => first);
        else Assert.Equal(3U, await first);
        Assert.Equal(fail ? 4 : 7, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
    }

    [Fact]
    public async Task WriteCorrectionAfterRacingSeekMatchesSignedNinefrontOffset()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        var pending = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plane.WriteOverride = (_, _, _, _, _) => new(pending.Task);
        Task<uint> writing = f.Calls.WriteAsync(fd, new byte[8]).AsTask();
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        pending.SetResult(3);
        await writing;
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.ReadAsync(fd, 1).AsTask());
        Assert.Equal(0, await f.Calls.SeekAsync(fd, 5, Plan9SeekWhence.Current));
    }

    [Fact]
    public async Task ExitDuringOpenRejectsPublicationEvenWhenChildSharesTable()
    {
        await using var f = new FileSyscallFixture();
        await f.OpenAsync();
        VProcess child = f.Table.Fork(f.Process.Id, NamespaceForkMode.Share);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plane.AfterOpen = _ => resume.Task;
        Task<int> opening = f.OpenAsync().AsTask();
        await f.Table.TerminateAsync(f.Process.Id);
        Assert.False(opening.IsCompleted);
        resume.SetResult();
        await Assert.ThrowsAsync<NamespaceException>(() => opening);
        Assert.Equal(1, f.Resources.ClunkCount);
        Assert.Single(child.Descriptors.Snapshot());
        Assert.Empty((await f.ForProcess(child).ReadAsync(0, 1)).ToArray());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.ReadAsync(0, 1).AsTask());
        await f.Table.TerminateAsync(child.Id);
        Assert.Equal(2, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task PendingReadRetainsOldChannelAfterCloseReuseAndExit()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.WriteAsync(fd, "old"u8.ToArray());
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plane.ReadOverride = async (handle, offset, count, token) =>
        {
            await resume.Task;
            return await f.Local.ReadAsync(handle, offset, count, token);
        };
        Task<ReadOnlyMemory<byte>> reading = f.Calls.ReadAsync(fd, 3).AsTask();
        await f.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(0, f.Resources.ClunkCount);
        Assert.Equal(fd, await f.Calls.OpenAsync("/other", new(2)));
        Assert.Equal(0, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        await f.Table.TerminateAsync(f.Process.Id);
        Assert.Equal(1, f.Resources.ClunkCount);
        resume.SetResult();
        Assert.Equal("old"u8.ToArray(), (await reading).ToArray());
        Assert.Equal(2, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task FullDescriptorTableClosesUnpublishedOpenAndPreservesOriginalError()
    {
        await using var f = new FileSyscallFixture();
        for (int fd = 0; fd < 5000; fd++)
            f.Process.Descriptors.Install(DescriptorGroupTests.Handle(fd.ToString()), () => ValueTask.CompletedTask);
        int attempts = 0;
        f.Plane.BeforeClunk = () => { attempts++; throw new IOException("cleanup failed"); };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.OpenAsync().AsTask());
        Assert.Equal(1, attempts);
        Assert.Equal(5000, f.Process.Descriptors.Snapshot().Count);
    }

    [Theory]
    [InlineData("/missing", 0)]
    [InlineData("/file/", 0)]
    [InlineData("/file/.", 0)]
    [InlineData("/", 1)]
    [InlineData("/", 16)]
    [InlineData("/file", 128)]
    public async Task InvalidOpenNeverAllocatesAProviderHandle(string path, byte mode)
    {
        await using var f = new FileSyscallFixture();
        f.Plane.AfterOpen = _ => throw new IOException("must not open");
        if (path.StartsWith("/file/", StringComparison.Ordinal) || path == "/missing")
            await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.OpenAsync(path, new(mode)).AsTask());
        else
            await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.OpenAsync(path, new(mode)).AsTask());
        Assert.Empty(f.Process.Descriptors.Snapshot());
    }

    [Fact]
    public async Task SeekEndNegativeInvalidAndOverflowPreservePosition()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.WriteAsync(fd, new byte[9]);
        Assert.Equal(7, await f.Calls.SeekAsync(fd, -2, Plan9SeekWhence.End));
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.SeekAsync(fd, -8, Plan9SeekWhence.Current).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.SeekAsync(fd, -1, Plan9SeekWhence.Set).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Calls.SeekAsync(fd, 0, (Plan9SeekWhence)3).AsTask());
        await Assert.ThrowsAsync<OverflowException>(() => f.Calls.SeekAsync(fd, long.MaxValue, Plan9SeekWhence.End).AsTask());
        Assert.Equal(7, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        Assert.Equal(long.MaxValue, await f.Calls.SeekAsync(fd, long.MaxValue, Plan9SeekWhence.Set));
        await Assert.ThrowsAsync<OverflowException>(() => f.Calls.SeekAsync(fd, 1, Plan9SeekWhence.Current).AsTask());
        await Assert.ThrowsAsync<OverflowException>(() => f.Calls.WriteAsync(fd, new byte[1]).AsTask());
        Assert.Equal(long.MaxValue, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
    }

    [Fact]
    public async Task DirectoryRewindAcceptedAndInvalidSeekOrWriteRejected()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.OpenAsync("/", new(NinePConstants.OCEXEC));
        Assert.Equal(0, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set));
        foreach (Plan9SeekWhence whence in new[] { Plan9SeekWhence.Current, Plan9SeekWhence.End })
            await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.SeekAsync(fd, 0, whence).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.SeekAsync(fd, 1, Plan9SeekWhence.Set).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.ReadAsync(fd, 1).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WriteAsync(fd, new byte[1]).AsTask());
    }

    [Fact]
    public async Task InvalidProviderCountsAndNegativePositionedIoDoNotAdvanceOffset()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.SeekAsync(fd, 5, Plan9SeekWhence.Set);
        f.Plane.ReadOverride = (_, _, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[2]);
        f.Plane.WriteOverride = (_, _, _, _, _) => ValueTask.FromResult(2U);
        foreach (long offset in new long[] { -1, 0 })
        {
            await Assert.ThrowsAsync<IOException>(() => f.Calls.PReadAsync(fd, offset, 1).AsTask());
            await Assert.ThrowsAsync<IOException>(() => f.Calls.PWriteAsync(fd, offset, new byte[1]).AsTask());
        }
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.PReadAsync(fd, -2, 1).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.PWriteAsync(fd, -2, new byte[1]).AsTask());
        Assert.Equal(5, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
    }

    [Property(MaxTest = 100)]
    public async Task GeneratedPositionedAndImplicitTransfersMatchIndependentOffsetModel(NonEmptyArray<byte> input)
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.PWriteAsync(fd, 0, new byte[1024]);
        int dup = await f.Process.Descriptors.DuplicateAsync(fd);
        long position = 0;
        foreach (byte value in input.Get.Take(100))
        {
            int count = value % 8;
            switch (value % 4)
            {
                case 0:
                    Assert.Equal((uint)count, await f.Calls.WriteAsync(dup, new byte[count]));
                    position += count;
                    break;
                case 1:
                    Assert.Equal(count, (await f.Calls.ReadAsync(fd, (uint)count)).Length);
                    position += count;
                    break;
                case 2:
                    Assert.Equal(count, (await f.Calls.PReadAsync(dup, value, (uint)count)).Length);
                    break;
                case 3:
                    Assert.Equal((uint)count, await f.Calls.PWriteAsync(fd, value, new byte[count]));
                    break;
            }
            Assert.Equal(position, await f.Calls.SeekAsync(dup, 0, Plan9SeekWhence.Current));
        }
    }
}

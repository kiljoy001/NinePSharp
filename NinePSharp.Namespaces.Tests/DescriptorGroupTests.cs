using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DescriptorGroupTests
{
    [Fact]
    public async Task RenumberMovesOwnershipAndFlagsAndClosesDisplacedSlotOutsideLock()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        int closed = 0;
        var source = Handle("moved");
        group.Install(source, () => { closed++; return ValueTask.CompletedTask; }, true);
        group.Install(Handle("displaced"), async () =>
        {
            await Task.Run(() => Assert.Equal(new DescriptorSlot(1, source, true), Assert.Single(group.Snapshot())));
            closed++;
        });
        await group.RenumberAsync(0, 1);
        Assert.Equal(1, closed);
        Assert.Throws<ArgumentException>(() => group.Acquire(0));
        await group.RenumberAsync(1, 1);
        Assert.Equal(1, closed);
        await group.RenumberAsync(1, 0);
        Assert.Equal(new DescriptorSlot(0, source, true), Assert.Single(group.Snapshot()));
        await group.RenumberAsync(0, 39);
        Assert.Equal(new DescriptorSlot(39, source, true), Assert.Single(group.Snapshot()));
        await table.TerminateAsync(process.Id);
        Assert.Equal(2, closed);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 40)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task FailedRenumberPreservesEverySlot(int source, int destination)
    {
        var (table, process) = CreateProcess();
        process.Descriptors.Install(Handle("existing"), () => ValueTask.CompletedTask, true);
        var before = process.Descriptors.Snapshot();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => process.Descriptors.RenumberAsync(source, destination).AsTask());
        Assert.Equal(before, process.Descriptors.Snapshot());
        await table.TerminateAsync(process.Id);
    }

    [Fact]
    public async Task SignedOffsetAccountingRejectsOverflowWithoutPublishingAWrappedPosition()
    {
        var (table, process) = CreateProcess();
        process.Descriptors.Install(Handle("offset"), () => ValueTask.CompletedTask);
        await using (DescriptorLease lease = process.Descriptors.Acquire(0))
        {
            lease.CorrectWrite(long.MaxValue);
            Assert.Equal(-long.MaxValue, lease.Offset);
            Assert.Throws<OverflowException>(() => lease.CorrectWrite(2));
            Assert.Equal(-long.MaxValue, lease.Offset);
            lease.Seek(long.MaxValue, false);
            Assert.Throws<OverflowException>(() => lease.AdvanceRead(1));
            Assert.Equal(long.MaxValue, lease.Offset);
        }
        await table.TerminateAsync(process.Id);
    }

    [Theory]
    [InlineData(NamespaceForkMode.Share, DescriptorForkMode.Share)]
    [InlineData(NamespaceForkMode.Copy, DescriptorForkMode.Share)]
    [InlineData(NamespaceForkMode.Empty, DescriptorForkMode.Share)]
    [InlineData(NamespaceForkMode.Share, DescriptorForkMode.Copy)]
    [InlineData(NamespaceForkMode.Copy, DescriptorForkMode.Copy)]
    [InlineData(NamespaceForkMode.Empty, DescriptorForkMode.Copy)]
    [InlineData(NamespaceForkMode.Share, DescriptorForkMode.Empty)]
    [InlineData(NamespaceForkMode.Copy, DescriptorForkMode.Empty)]
    [InlineData(NamespaceForkMode.Empty, DescriptorForkMode.Empty)]
    public async Task ForkModesIndependentlyControlNamespaceAndDescriptors(NamespaceForkMode ns, DescriptorForkMode fd)
    {
        var (table, parent) = CreateProcess();
        int closed = 0;
        var handle = Handle("inherited");
        var original = parent.Descriptors;
        original.Install(handle, () => { closed++; return ValueTask.CompletedTask; }, closeOnExec: true);
        var child = table.Fork(parent.Id, ns, descriptorMode: fd);
        Assert.Equal(ns == NamespaceForkMode.Share, ReferenceEquals(parent.ProcessGroup, child.ProcessGroup));
        Assert.Equal(fd == DescriptorForkMode.Share, ReferenceEquals(original, child.Descriptors));
        Assert.Equal(fd == DescriptorForkMode.Share ? 2 : 1, original.OwnerCount);
        if (fd == DescriptorForkMode.Empty) Assert.Empty(child.Descriptors.Snapshot());
        else Assert.Equal(new DescriptorSlot(0, handle, true), Assert.Single(child.Descriptors.Snapshot()));

        await original.CloseAsync(0);
        Assert.Equal(fd == DescriptorForkMode.Copy ? 0 : 1, closed);
        Assert.Equal(fd == DescriptorForkMode.Copy ? 1 : 0, child.Descriptors.Snapshot().Count);
        Assert.True(await table.TerminateAsync(parent.Id));
        Assert.True(await table.TerminateAsync(child.Id));
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData(DescriptorForkMode.Share)]
    [InlineData(DescriptorForkMode.Copy)]
    [InlineData(DescriptorForkMode.Empty)]
    public async Task RforkDescriptorsChangesOnlyCurrentMembership(DescriptorForkMode mode)
    {
        var (table, parent) = CreateProcess();
        int closed = 0;
        var original = parent.Descriptors;
        original.Install(Handle("source"), () => { closed++; return ValueTask.CompletedTask; }, true);
        var child = table.Fork(parent.Id, NamespaceForkMode.Share);
        var ns = parent.ProcessGroup;
        await table.RforkDescriptorsAsync(parent.Id, mode);
        Assert.Same(ns, parent.ProcessGroup);
        Assert.Same(original, child.Descriptors);
        Assert.Equal(mode == DescriptorForkMode.Share, ReferenceEquals(original, parent.Descriptors));
        Assert.Equal(mode == DescriptorForkMode.Share ? 2 : 1, original.OwnerCount);
        Assert.Equal(mode == DescriptorForkMode.Share ? 2 : 1, parent.Descriptors.OwnerCount);
        Assert.Equal(mode == DescriptorForkMode.Empty ? 0 : 1, parent.Descriptors.Snapshot().Count);
        await table.TerminateAsync(child.Id);
        Assert.Equal(mode == DescriptorForkMode.Empty ? 1 : 0, closed);
        await table.TerminateAsync(parent.Id);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task ParentExitPreservesSharedTableAndFinalExitClosesEverySlot()
    {
        var (table, parent) = CreateProcess();
        var group = parent.Descriptors;
        var closed = new List<string>();
        group.Install(Handle("first"), () => { lock (closed) closed.Add("first"); return ValueTask.CompletedTask; });
        group.Install(Handle("second"), () => { lock (closed) closed.Add("second"); return ValueTask.CompletedTask; });
        var child = table.Fork(parent.Id, NamespaceForkMode.Copy);
        Assert.False(parent.TerminationCompletion.IsCompleted);
        Assert.True(table.Terminate(parent.Id));
        await parent.TerminationCompletion;
        Assert.False(group.IsClosed);
        Assert.Equal(1, group.OwnerCount);
        Assert.Empty(closed);
        await using (var lease = child.Descriptors.Acquire(1)) Assert.Equal("second", lease.Handle.HandleId);
        Assert.True(await table.TerminateAsync(child.Id));
        Assert.False(await table.TerminateAsync(child.Id));
        Assert.True(group.IsClosed);
        Assert.Equal(0, group.OwnerCount);
        Assert.Empty(group.Snapshot());
        Assert.Equal(new[] { "first", "second" }, closed.Order());
        Assert.Throws<NamespaceException>(() => parent.Descriptors);
        Assert.Throws<ObjectDisposedException>(() => group.Acquire(0));
        Assert.Throws<ObjectDisposedException>(() => group.Install(Handle("late"), () => ValueTask.CompletedTask));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => group.CloseOnExecAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => group.DuplicateAsync(0).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => parent.RforkDescriptorsAsync(DescriptorForkMode.Empty));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => table.RforkDescriptorsAsync(child.Id, DescriptorForkMode.Share));
    }

    [Fact]
    public async Task DupClearsFlagsAndRetainsSourceBeforeDestinationCloseIncludingSelfDup()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        int sourceCloses = 0, displacedCloses = 0;
        var source = Handle("source");
        group.Install(source, () => { sourceCloses++; return ValueTask.CompletedTask; }, true);
        group.Install(Handle("displaced"), () => { displacedCloses++; return ValueTask.CompletedTask; }, true);
        Assert.Equal(1, await group.DuplicateAsync(0, 1));
        Assert.Equal(1, displacedCloses);
        Assert.Equal(new DescriptorSlot(1, source, false), group.Snapshot()[1]);
        Assert.True(group.Snapshot()[0].CloseOnExec);
        Assert.Equal(0, await group.DuplicateAsync(0, 0));
        Assert.False(group.Snapshot()[0].CloseOnExec);
        Assert.Equal(0, sourceCloses);
        await group.CloseAsync(0);
        Assert.Equal(0, await group.DuplicateAsync(1));
        Assert.Equal(2, await group.DuplicateAsync(1));
        await table.TerminateAsync(process.Id);
        Assert.Equal(1, sourceCloses);
        Assert.Equal(1, displacedCloses);
    }

    [Fact]
    public async Task InvalidDupAndForkLeaveExistingOwnershipUntouched()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        var handle = Handle("source");
        group.Install(handle, () => ValueTask.CompletedTask, true);
        await Assert.ThrowsAsync<ArgumentException>(() => group.DuplicateAsync(4, 0).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => group.DuplicateAsync(0, -2).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => group.DuplicateAsync(0, int.MaxValue).AsTask());
        foreach (int invalid in new[] { -1, 3 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => table.Fork(process.Id, NamespaceForkMode.Copy, descriptorMode: (DescriptorForkMode)invalid));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => table.RforkDescriptorsAsync(process.Id, (DescriptorForkMode)invalid));
        }
        Assert.Single(table.Snapshot());
        Assert.Equal(1, group.OwnerCount);
        Assert.Equal(new DescriptorSlot(0, handle, true), Assert.Single(group.Snapshot()));
        Assert.Throws<ArgumentNullException>(() => group.Install(null!, () => ValueTask.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => group.Install(handle, null!));
        await table.TerminateAsync(process.Id);
    }

    [Fact]
    public async Task CapacityGrowthAndCopyMatchNinefrontAllocationBoundaries()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        group.Install(Handle("source"), () => ValueTask.CompletedTask);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => group.DuplicateAsync(0, 40).AsTask());
        Assert.Equal(19, await group.DuplicateAsync(0, 19));
        Assert.Equal(39, await group.DuplicateAsync(0, 39));
        await group.CloseAsync(39);
        var child = table.Fork(process.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        // dupfgrp shrinks the copy to 20; closing a slot does not shrink the original.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => child.Descriptors.DuplicateAsync(0, 40).AsTask());
        Assert.Equal(39, await child.Descriptors.DuplicateAsync(0, 39));
        var grandchild = table.Fork(child.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        Assert.Equal(59, await grandchild.Descriptors.DuplicateAsync(0, 59));
        Assert.Equal(59, await group.DuplicateAsync(0, 59));
        for (int fd = 79; fd < 5000; fd += 20) await group.DuplicateAsync(0, fd);
        Assert.Equal(4999, await group.DuplicateAsync(0, 4999));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => group.DuplicateAsync(0, 5000).AsTask());
        await table.TerminateAsync(child.Id);
        await table.TerminateAsync(grandchild.Id);
        await table.TerminateAsync(process.Id);
    }

    [Fact]
    public async Task InstallFillsHolesAndRejectsAFullTableWithoutTakingOwnership()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        int closed = 0;
        for (int fd = 0; fd < 5000; fd++)
            Assert.Equal(fd, group.Install(Handle(fd.ToString()), () => { Interlocked.Increment(ref closed); return ValueTask.CompletedTask; }));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.Install(Handle("unowned"), () => throw new InvalidOperationException("ownership must stay with caller")));
        Assert.Equal(0, closed);
        await group.CloseAsync(0);
        Assert.Equal(0, group.Install(Handle("reused"), () => { Interlocked.Increment(ref closed); return ValueTask.CompletedTask; }));
        await table.TerminateAsync(process.Id);
        Assert.Equal(5001, closed);
    }

    [Fact]
    public async Task InFlightLeasePinsChannelAfterFinalExitAndRepeatedDisposalIsSafe()
    {
        var (table, process) = CreateProcess();
        int closed = 0;
        var group = process.Descriptors;
        group.Install(Handle("pinned"), () => { closed++; return ValueTask.CompletedTask; });
        var lease = group.Acquire(0);
        await table.TerminateAsync(process.Id);
        Assert.Equal(0, closed);
        Assert.Equal("pinned", lease.Handle.HandleId);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task RepeatedLeaseDisposalCannotReleaseALiveDescriptorSlot()
    {
        var (table, process) = CreateProcess();
        int closed = 0;
        process.Descriptors.Install(Handle("live-slot"), () => { closed++; return ValueTask.CompletedTask; });
        var lease = process.Descriptors.Acquire(0);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(0, closed);
        await using (var next = process.Descriptors.Acquire(0)) Assert.Equal("live-slot", next.Handle.HandleId);
        Assert.Equal(0, closed);
        await table.TerminateAsync(process.Id);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task ProviderCloseCanWaitForAnotherThreadToInspectTheProcessTable()
    {
        var (table, process) = CreateProcess();
        bool inspected = false;
        Task? inspection = null;
        process.Descriptors.Install(Handle("callback"), () =>
        {
            // A provider can synchronously consult another service/thread during
            // close. Keeping the process-table lock here would deadlock that work.
            inspection = Task.Run(() => Assert.Empty(table.Snapshot()));
            inspected = inspection.Wait(TimeSpan.FromSeconds(5));
            return ValueTask.CompletedTask;
        });
        await table.TerminateAsync(process.Id);
        Assert.True(inspected);
        await inspection!;
    }

    [Fact]
    public async Task TerminationDetachesBeforeAwaitingProviderAndContinuesAfterCloseFailure()
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int otherClosed = 0;
        group.Install(Handle("blocked"), async () => { entered.SetResult(); await resume.Task; throw new IOException("provider close failed"); });
        group.Install(Handle("other"), () => { Interlocked.Increment(ref otherClosed); return ValueTask.CompletedTask; });
        var exiting = table.TerminateAsync(process.Id);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(process.IsTerminated);
            Assert.True(group.IsClosed);
            Assert.Empty(group.Snapshot());
            Assert.False(exiting.IsCompleted);
            Assert.False(process.TerminationCompletion.IsCompleted);
            Assert.Empty(table.Snapshot());
        }
        finally { resume.TrySetResult(); }
        Assert.True(await exiting);
        await process.TerminationCompletion;
        Assert.Equal(1, otherClosed);
    }

    [Fact]
    public async Task CloseOnExecPreservesCopyFlagsAndRemovesSharedSlotsOnlyWhenRequested()
    {
        var (table, parent) = CreateProcess();
        int closed = 0;
        var group = parent.Descriptors;
        group.Install(Handle("marked"), () => { closed++; return ValueTask.CompletedTask; }, true);
        await group.DuplicateAsync(0, 1);
        var copy = table.Fork(parent.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Copy);
        var shared = table.Fork(parent.Id, NamespaceForkMode.Copy);
        await group.CloseOnExecAsync();
        Assert.Equal(1, Assert.Single(shared.Descriptors.Snapshot()).Number);
        Assert.Equal(2, copy.Descriptors.Snapshot().Count);
        Assert.True(copy.Descriptors.Snapshot()[0].CloseOnExec);
        await copy.Descriptors.CloseOnExecAsync();
        Assert.Equal(1, Assert.Single(copy.Descriptors.Snapshot()).Number);
        await table.TerminateAsync(parent.Id);
        await table.TerminateAsync(shared.Id);
        Assert.Equal(0, closed);
        await table.TerminateAsync(copy.Id);
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalTableReleaseAndExecCloseSlotsInAscendingOrder(bool exec)
    {
        var (table, process) = CreateProcess();
        var group = process.Descriptors;
        var order = new List<int>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        group.Install(Handle("first"), async () =>
        {
            entered.SetResult();
            await resume.Task;
            lock (order) order.Add(0);
        }, true);
        group.Install(Handle("second"), () =>
        {
            lock (order) order.Add(1);
            return ValueTask.CompletedTask;
        }, true);
        Task cleanup = exec ? group.CloseOnExecAsync() : table.TerminateAsync(process.Id);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(group.Snapshot());
            lock (order) Assert.Empty(order);
        }
        finally { resume.TrySetResult(); }
        await cleanup;
        Assert.Equal(new[] { 0, 1 }, order);
        if (exec) await table.TerminateAsync(process.Id);
    }

    [Fact]
    public async Task ClosedDescriptorGroupCannotBeAdoptedAndConstructorRollsBackNamespaceRetain()
    {
        var (table, process) = CreateProcess();
        var closed = process.Descriptors;
        var root = process.Root;
        await table.TerminateAsync(process.Id);
        var parent = table.CreateInitial(root);
        Assert.Throws<ObjectDisposedException>(() => new VProcess(100, null, parent.ProcessGroup, root, root, closed));
        Assert.Equal(1, parent.ProcessGroup.OwnerCount);
        Assert.False(parent.ProcessGroup.MountTable.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => closed.Copy());
        await table.TerminateAsync(parent.Id);
    }

    [Property(MaxTest = 100)]
    public async Task DuplicatedChannelClosesOnlyAfterEveryGeneratedHolderIsReleased(NonEmptyArray<byte> input)
    {
        var (table, parent) = CreateProcess();
        int closed = 0;
        parent.Descriptors.Install(Handle("shared-instance"), () => { Interlocked.Increment(ref closed); return ValueTask.CompletedTask; });
        var holders = new List<VProcess> { parent };
        var groups = new HashSet<DescriptorGroup> { parent.Descriptors };
        foreach (byte value in input.Get.Take(40))
        {
            var child = table.Fork(parent.Id, (NamespaceForkMode)(value % 3), descriptorMode: (DescriptorForkMode)(value / 3 % 3));
            holders.Add(child);
            groups.Add(child.Descriptors);
            if (child.Descriptors.Snapshot().Count != 0) await child.Descriptors.DuplicateAsync(0);
        }

        foreach (byte value in input.Get.Take(40))
        {
            if (holders.Count == 1) break;
            var selected = holders[value % holders.Count];
            await table.TerminateAsync(selected.Id);
            holders.Remove(selected);
            foreach (var group in groups)
            {
                int expectedOwners = holders.Count(p => ReferenceEquals(p.Descriptors, group));
                Assert.Equal(expectedOwners, group.OwnerCount);
                Assert.Equal(expectedOwners == 0, group.IsClosed);
            }
            bool referenced = groups.Any(g => g.Snapshot().Count != 0);
            Assert.Equal(referenced ? 0 : 1, closed);
        }

        foreach (var holder in holders) await table.TerminateAsync(holder.Id);
        Assert.Equal(1, closed);
    }

    internal static (VProcessTable Table, VProcess Process) CreateProcess()
    {
        var table = new VProcessTable();
        var root = new MemoryResources().Directory("root");
        return (table, table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) })));
    }

    internal static ResourceOpenHandle Handle(string id)
        => new(new ResourceHandle(new ResourceIdentity("descriptor-tests", "file", 1), QidType.QTFILE), id, NinePConstants.ORDWR | NinePConstants.ORCLOSE, 0);
}

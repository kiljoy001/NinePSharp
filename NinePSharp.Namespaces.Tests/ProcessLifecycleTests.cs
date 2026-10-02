using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class ProcessLifecycleTests
{
    [Fact]
    public void TerminationReleasesRootAndCurrentChannelReferences()
    {
        var (process, root, cwd) = TerminatedProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(root.IsAlive);
        Assert.False(cwd.IsAlive);
        GC.KeepAlive(process);
    }

    [Fact]
    public async Task LastOwnerClosesNamespaceAndRejectsStaleProcessAndNavigatorReferences()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var target = resources.Directory("target");
        var table = new VProcessTable();
        var process = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var group = process.ProcessGroup;
        var mounts = group.MountTable;
        mounts.Mount(target, root);
        mounts.SetMountsDisabled(true);
        var navigator = new NamespaceNavigator(mounts, resources);
        var channel = navigator.Attach(root);

        Assert.True(table.Terminate(process.Id));
        Assert.False(table.Terminate(process.Id));
        Assert.True(process.IsTerminated);
        Assert.Equal(0, group.OwnerCount);
        Assert.True(mounts.IsClosed);
        Assert.Empty(mounts.Snapshot().MountHeads);
        Assert.Empty(table.Snapshot());
        Assert.Throws<KeyNotFoundException>(() => table.Get(process.Id));
        Assert.Throws<KeyNotFoundException>(() => table.Fork(process.Id, NamespaceForkMode.Share));
        Assert.Throws<KeyNotFoundException>(() => table.RforkNamespace(process.Id, NamespaceForkMode.Share));
        Closed(() => _ = process.Root);
        Closed(() => _ = process.CurrentDirectory);
        Closed(() => _ = process.ProcessGroup);
        Closed(() => process.ChangeDirectory(channel));
        var unused = new VProcessGroup(999);
        Closed(() => process.ReplaceProcessGroup(unused));
        Assert.Equal(0, unused.OwnerCount);
        Closed(() => mounts.Mount(target, root));
        Closed(() => mounts.Mount(channel, root));
        Closed(() => mounts.Unmount(root));
        Closed(() => mounts.Find(root.Identity));
        Closed(() => mounts.Clone());
        Closed(() => mounts.SetMountsDisabled(true));
        Closed(() => mounts.SetMountDeviceBlocked("x", true));
        Closed(() => navigator.Attach(root));
        Closed(() => navigator.SelectCreateTarget(channel));
        var unmountedChannel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) });
        Closed(() => mounts.Mount(unmountedChannel, root));
        Closed(() => navigator.SelectCreateTarget(unmountedChannel));
        await ClosedAsync(() => navigator.WalkAsync(channel, Array.Empty<string>()).AsTask());
        await ClosedAsync(() => navigator.WalkAsync(channel, new[] { "." }).AsTask());
        await ClosedAsync(() => navigator.ReadDirectoryAsync(channel).AsTask());
        Closed(() => new VProcess(100, null, group, channel, channel));
        Assert.Equal(0, group.OwnerCount);
        var next = table.CreateInitial(channel);
        Assert.True(next.Id > process.Id);
        Assert.False(next.IsTerminated);
    }

    [Fact]
    public void SwitchingGroupsRetainsBeforeReleaseAndDoesNotCloseSharedOwners()
    {
        var table = new VProcessTable();
        var root = new MemoryResources().Directory("root");
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) });
        var parent = table.CreateInitial(channel);
        var original = parent.ProcessGroup;
        parent.ReplaceProcessGroup(original);
        Assert.Equal(1, original.OwnerCount);
        Assert.False(original.MountTable.IsClosed);
        var child = table.Fork(parent.Id, NamespaceForkMode.Share);
        Assert.Equal(2, original.OwnerCount);
        table.RforkNamespace(parent.Id, NamespaceForkMode.Copy);
        Assert.Equal(1, original.OwnerCount);
        Assert.False(original.MountTable.IsClosed);
        var copied = parent.ProcessGroup;
        table.RforkNamespace(parent.Id, NamespaceForkMode.Empty);
        Assert.Equal(0, copied.OwnerCount);
        Assert.True(copied.MountTable.IsClosed);
        Closed(() => parent.ReplaceProcessGroup(copied));
        Assert.Equal(1, parent.ProcessGroup.OwnerCount);
        table.Terminate(child.Id);
        Assert.True(original.MountTable.IsClosed);
        Assert.False(parent.ProcessGroup.MountTable.IsClosed);
        table.Terminate(parent.Id);
    }

    [Property(MaxTest = 100)]
    public void TerminationOrderClosesExactlyTheGroupsWithoutOwners(NonEmptyArray<byte> operations)
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var target = resources.Directory("target");
        var table = new VProcessTable();
        var parent = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        parent.ProcessGroup.MountTable.Mount(target, root);
        var live = new List<VProcess> { parent };
        var groups = new HashSet<VProcessGroup> { parent.ProcessGroup };
        foreach (byte operation in operations.Get.Take(100))
        {
            if (live.Count == 0)
            {
                break;
            }

            var selected = live[operation % live.Count];
            switch (operation % 3)
            {
                case 0:
                    var child = table.Fork(selected.Id, (NamespaceForkMode)((operation / 3) % 3));
                    live.Add(child);
                    groups.Add(child.ProcessGroup);
                    break;
                case 1:
                    table.RforkNamespace(selected.Id, (NamespaceForkMode)((operation / 3) % 3));
                    groups.Add(selected.ProcessGroup);
                    break;
                default:
                    Assert.True(table.Terminate(selected.Id));
                    live.Remove(selected);
                    break;
            }

            foreach (var group in groups)
            {
                int owners = live.Count(process => ReferenceEquals(process.ProcessGroup, group));
                Assert.Equal(owners, group.OwnerCount);
                Assert.Equal(owners == 0, group.MountTable.IsClosed);
                if (owners == 0)
                {
                    Assert.Empty(group.MountTable.Snapshot().MountHeads);
                }
            }
        }

        foreach (var process in live)
        {
            table.Terminate(process.Id);
        }

        Assert.All(groups, group => Assert.True(group.MountTable.IsClosed));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (VProcess Process, WeakReference Root, WeakReference Cwd) TerminatedProcess()
    {
        var resources = new MemoryResources();
        var channel = new NamespaceNavigator(new MountTable(), resources).Attach(resources.Directory("root"));
        var table = new VProcessTable();
        var process = table.CreateInitial(channel);
        var root = new WeakReference(process.Root);
        var cwd = new WeakReference(process.CurrentDirectory);
        table.Terminate(process.Id);
        return (process, root, cwd);
    }

    private static void Closed(Action action)
        => Assert.Equal(NamespaceError.NamespaceClosed, Assert.Throws<NamespaceException>(action).Error);

    private static async Task ClosedAsync(Func<Task> action)
        => Assert.Equal(NamespaceError.NamespaceClosed, (await Assert.ThrowsAsync<NamespaceException>(action)).Error);
}

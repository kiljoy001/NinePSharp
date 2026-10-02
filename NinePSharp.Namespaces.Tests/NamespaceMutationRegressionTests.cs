using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceMutationRegressionTests
{
    [Fact]
    public void NewlyMountedTargetTakesPrecedenceOverAnUncrossedFramesSavedUnion()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var saved = resources.Directory("saved");
        var live = resources.Directory("live");
        var mounts = new MountTable();
        var channel = NamespaceChannel.Restore(new[]
        {
            new ChannelFrame("/", root, null, new[] { new MountBinding(1, MountFlags.Create, saved, string.Empty) }),
        });
        mounts.Mount(live, root, MountFlags.Create);
        Assert.Equal(live, new NamespaceNavigator(mounts, resources).SelectCreateTarget(channel));
    }

    [Fact]
    public void DevicePolicySnapshotsAreSortedAndRejectMismatchedSourceIdentity()
    {
        var table = new MountTable();
        table.SetMountDeviceBlocked("z", true);
        table.SetMountDeviceBlocked("a", true);
        Assert.Equal(new[] { "a", "z" }, table.BlockedMountDevices);
        Assert.Equal(new[] { "a", "z" }, table.Snapshot().BlockedMountDevices);
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var other = resources.Directory("other");
        Assert.Throws<ArgumentNullException>(() => table.Mount(null!, other, MountFlags.Replace, null, null));
        Assert.Throws<ArgumentException>(() => table.Mount(root, other, MountFlags.Replace, null, new[] { new MountBinding(1, MountFlags.Replace, other, string.Empty) }));
    }

    [Fact]
    public async Task CreateTargetRechecksLiveMountsAndCreatedChildrenRetainProviderIdentity()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var target = resources.Directory("target");
        var replacement = resources.Directory("replacement");
        var table = new MountTable();
        table.Mount(target, root, MountFlags.Replace | MountFlags.Create);
        var navigator = new NamespaceNavigator(table, resources);
        var channel = navigator.Attach(root);
        table.Mount(replacement, root, MountFlags.Replace | MountFlags.Create);
        Assert.Equal(replacement, navigator.SelectCreateTarget(channel));
        Assert.Equal(root, navigator.EnterCreated(navigator.Attach(resources.Directory("parent")), "child", root).Current);
        var plane = new LocalNamespaceDataPlane(table, new MemoryDataResources());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plane.AttachAsync(root, new CancellationToken(true)).AsTask());
        Assert.Throws<KeyNotFoundException>(() => new VProcessTable().RforkNamespace(99, NamespaceForkMode.Copy));
    }

    [Fact]
    public async Task AbsolutePathsUseRootWhenCwdDiffersAndMissingParentsFail()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var source = resources.AddChild(root, "source", true);
        var target = resources.AddChild(root, "target", true);
        var cwd = resources.AddChild(root, "cwd", true);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ChangeDirectory(new NamespaceNavigator(new MountTable(), resources).Attach(cwd));
        var syscalls = new NamespaceSyscalls(resources);
        await syscalls.BindAsync(process, "/source", "/target");
        Assert.Equal(source, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target);
        await Assert.ThrowsAsync<NamespaceException>(() => syscalls.BindAsync(process, "/source", "/missing/target").AsTask());
        await syscalls.BindAsync(process, "/source", "/");
        Assert.Equal(source, process.ProcessGroup.MountTable.Find(root.Identity)!.Mounts[0].Target);
    }
}

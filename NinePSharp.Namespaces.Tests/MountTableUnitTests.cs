using NinePSharp.Constants;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class MountTableUnitTests
{
    [Fact]
    public void MountRejectsNullAndInvalidArguments()
    {
        var table = new MountTable();
        ResourceHandle directory = Directory("directory", 1);
        ResourceHandle file = File("file", 2);

        Assert.Throws<ArgumentNullException>(() => table.Mount((ResourceHandle)null!, directory));
        Assert.Throws<ArgumentNullException>(() => table.Mount(directory, (ResourceHandle)null!));
        Assert.Throws<ArgumentNullException>(() => table.Mount((NamespaceChannel)null!, directory));
        Assert.Throws<ArgumentNullException>(() => table.Mount(Channel(directory), null!));
        AssertError(NamespaceError.InvalidMountFlags, () => table.Mount(directory, directory, MountFlags.Before | MountFlags.After));
        AssertError(NamespaceError.InvalidMountFlags, () => table.Mount(directory, directory, (MountFlags)8));
        AssertError(NamespaceError.MountTypeMismatch, () => table.Mount(file, directory));
        AssertError(NamespaceError.UnionRequiresDirectory, () => table.Mount(file, file, MountFlags.Before));
    }

    [Fact]
    public void BindingARestoredUnionCopiesOrderFlagsSpecsAndFreshIds()
    {
        ResourceHandle first = Directory("first", 1);
        ResourceHandle second = Directory("second", 2);
        ResourceHandle mountedOn = Directory("destination", 3);
        var sourceMounts = new[]
        {
            new MountBinding(40, MountFlags.Before, first, "first-spec"),
            new MountBinding(70, MountFlags.After | MountFlags.Create, second, "second-spec"),
        };
        NamespaceChannel source = NamespaceChannel.Restore(
            new[] { new ChannelFrame("/", first, null, sourceMounts) });
        var table = new MountTable();

        MountBinding result = table.Mount(source, mountedOn, MountFlags.Replace, "bind-spec");
        MountHead head = Assert.IsType<MountHead>(table.Find(mountedOn.Identity));

        Assert.Equal(1, result.MountId);
        Assert.Equal("bind-spec", result.Spec);
        Assert.Equal(new[] { first.Identity, second.Identity }, head.Mounts.Select(mount => mount.Target.Identity));
        Assert.Equal(new[] { MountFlags.Replace, MountFlags.After }, head.Mounts.Select(mount => mount.Flags));
        Assert.Equal(new[] { "bind-spec", "second-spec" }, head.Mounts.Select(mount => mount.Spec));
        Assert.Equal(new long[] { 1, 2 }, head.Mounts.Select(mount => mount.MountId));
    }

    [Fact]
    public void CreatableBindRejectsMultiMemberOrNonCreatableMountedSources()
    {
        ResourceHandle first = Directory("first", 1);
        ResourceHandle second = Directory("second", 2);
        ResourceHandle destination = Directory("destination", 3);
        var table = new MountTable();
        NamespaceChannel multiple = Channel(
            first,
            new MountBinding(1, MountFlags.Create, first, string.Empty),
            new MountBinding(2, MountFlags.After, second, string.Empty));
        NamespaceChannel notCreatable = Channel(
            first,
            new MountBinding(1, MountFlags.Replace, first, string.Empty));

        AssertError(
            NamespaceError.CreateBindNotPermitted,
            () => table.Mount(multiple, destination, MountFlags.Before | MountFlags.Create));
        AssertError(
            NamespaceError.CreateBindNotPermitted,
            () => table.Mount(notCreatable, destination, MountFlags.Before | MountFlags.Create));
    }

    [Fact]
    public void SingleCreatableMountedSourceCanBeBoundForCreation()
    {
        ResourceHandle sourceRoot = Directory("source", 1);
        ResourceHandle destination = Directory("destination", 2);
        NamespaceChannel source = Channel(
            sourceRoot,
            new MountBinding(9, MountFlags.Create, sourceRoot, "source"));
        var table = new MountTable();

        table.Mount(source, destination, MountFlags.Before | MountFlags.Create);

        Assert.Equal(sourceRoot.Identity, table.SelectCreateTarget(destination.Identity).Identity);
    }

    [Fact]
    public void BindUsesTheLiveMountHeadAndPreservesBeforeOrderingForCopiedMembers()
    {
        ResourceHandle original = Directory("original", 1);
        ResourceHandle first = Directory("first", 2);
        ResourceHandle latest = Directory("latest", 3);
        ResourceHandle destination = Directory("destination", 4);
        var table = new MountTable();
        table.Mount(first, original, MountFlags.Before);
        NamespaceChannel openSource = new NamespaceNavigator(table, new UnusedResources()).Attach(original);
        table.Mount(latest, original, MountFlags.Before);

        table.Mount(openSource, destination, MountFlags.Before);

        MountHead head = Assert.IsType<MountHead>(table.Find(destination.Identity));
        Assert.Equal(
            new[] { latest.Identity, first.Identity, original.Identity, destination.Identity },
            head.Mounts.Select(mount => mount.Target.Identity));
        Assert.Equal(
            new[] { MountFlags.Before, MountFlags.Before, MountFlags.Before, MountFlags.Replace },
            head.Mounts.Select(mount => mount.Flags));
    }

    [Fact]
    public void UnmountSupportsAllSelectedMissingAndLastMemberCases()
    {
        ResourceHandle mountedOn = Directory("point", 1);
        ResourceHandle first = Directory("first", 2);
        ResourceHandle missing = Directory("missing", 3);
        var table = new MountTable();

        Assert.Throws<ArgumentNullException>(() => table.Unmount(null!));
        AssertError(NamespaceError.MountNotFound, () => table.Unmount(mountedOn));
        table.Mount(first, mountedOn);
        AssertError(NamespaceError.UnionMemberNotFound, () => table.Unmount(mountedOn, missing));
        table.Unmount(mountedOn, first);
        Assert.Null(table.Find(mountedOn.Identity));

        table.Mount(first, mountedOn);
        table.Unmount(mountedOn);
        Assert.Null(table.Find(mountedOn.Identity));
    }

    [Fact]
    public void FindAndCreateSelectionUseStableIdentityAndFirstCreatableMember()
    {
        ResourceHandle mountedOn = Directory("point", 1, version: 1);
        ResourceHandle first = Directory("first", 2);
        ResourceHandle second = Directory("second", 3);
        var table = new MountTable();

        Assert.Throws<ArgumentNullException>(() => table.Find(null!));
        AssertError(NamespaceError.CreateNotPermitted, () => table.SelectCreateTarget(mountedOn.Identity));
        table.Mount(first, mountedOn, MountFlags.Before);
        AssertError(NamespaceError.CreateNotPermitted, () => table.SelectCreateTarget(mountedOn.Identity));
        table.Mount(second, mountedOn, MountFlags.After | MountFlags.Create);

        ResourceIdentity sameObjectNewVersion = Directory("point", 1, version: 99).Identity;
        Assert.Equal(second.Identity, table.SelectCreateTarget(sameObjectNewVersion).Identity);
    }

    [Fact]
    public void ClonePreservesAllocationOrderWhileAssigningFreshNamespaceIds()
    {
        ResourceHandle pointA = Directory("point-a", 1);
        ResourceHandle pointB = Directory("point-b", 2);
        ResourceHandle first = Directory("first", 3);
        ResourceHandle second = Directory("second", 4);
        ResourceHandle third = Directory("third", 5);
        var table = new MountTable();
        table.Mount(first, pointA);
        table.Mount(second, pointB);
        table.Mount(third, pointA, MountFlags.Before);

        MountTable clone = table.Clone();
        MountHead cloneA = Assert.IsType<MountHead>(clone.Find(pointA.Identity));
        MountHead cloneB = Assert.IsType<MountHead>(clone.Find(pointB.Identity));

        Assert.Equal(new long[] { 3, 1 }, cloneA.Mounts.Select(mount => mount.MountId));
        Assert.Equal(2, Assert.Single(cloneB.Mounts).MountId);
        Assert.Equal(3, clone.Snapshot().NextMountId);
    }

    [Fact]
    public void SnapshotsAreIndependentAndRejectNull()
    {
        ResourceHandle mountedOn = Directory("point", 1);
        ResourceHandle target = Directory("target", 2);
        var table = new MountTable();
        table.Mount(target, mountedOn, spec: null);

        NamespaceSnapshot snapshot = table.Snapshot();
        MountTable restored = MountTable.FromSnapshot(snapshot);
        table.Unmount(mountedOn);

        Assert.Equal(string.Empty, Assert.Single(restored.Find(mountedOn.Identity)!.Mounts).Spec);
        MountBinding next = restored.Mount(Directory("next", 4), Directory("next-point", 3));
        Assert.Equal(snapshot.NextMountId + 1, next.MountId);
        Assert.Throws<ArgumentNullException>(() => MountTable.FromSnapshot(null!));
    }

    private sealed class UnusedResources : IResourceOperations
    {
        public ValueTask<ResourceHandle?> WalkAsync(
            ResourceHandle directory,
            string name,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
            ResourceHandle directory,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<ResourceHandle> CreateAsync(
            ResourceHandle directory,
            string name,
            bool directoryEntry,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private static NamespaceChannel Channel(ResourceHandle handle, params MountBinding[] mounts)
        => NamespaceChannel.Restore(new[] { new ChannelFrame("/", handle, null, mounts) });

    private static ResourceHandle Directory(string device, ulong path, uint version = 0)
        => new(new ResourceIdentity("test", device, path), QidType.QTDIR, version);

    private static ResourceHandle File(string device, ulong path)
        => new(new ResourceIdentity("test", device, path), QidType.QTFILE);

    private static void AssertError(NamespaceError error, Action action)
        => Assert.Equal(error, Assert.Throws<NamespaceException>(action).Error);
}

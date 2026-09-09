using NinePSharp.Constants;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class NamespaceModelEqualityTests
{
    [Fact]
    public void EquivalentSnapshotsCompareArrayContentsAndIgnoreHeadOrder()
    {
        NamespaceSnapshotModel first = Snapshot(3, Head("one", 1), Head("two", 2));
        NamespaceSnapshotModel second = Snapshot(3, Head("two", 2), Head("one", 1));

        Assert.True(first.EquivalentTo(second));
        Assert.False(first.EquivalentTo(second with { NextMountId = 4 }));
        Assert.False(first.EquivalentTo(Snapshot(3, Head("one", 1))));
        Assert.False(first.EquivalentTo(Snapshot(3, Head("one", 1), Head("other", 2))));
        Assert.False(first.EquivalentTo(Snapshot(3, Head("one", 99), Head("two", 2))));
    }

    [Fact]
    public void EquivalentChannelsCompareFramesAndOptionalUnion()
    {
        NamespaceChannelModel first = Channel("root", true);
        NamespaceChannelModel same = Channel("root", true);

        Assert.True(first.EquivalentTo(first));
        Assert.True(first.EquivalentTo(same));
        Assert.False(first.EquivalentTo(new NamespaceChannelModel(Array.Empty<ChannelFrameModel>())));
        Assert.False(first.EquivalentTo(Channel("other", true)));
        Assert.False(first.EquivalentTo(Channel("root", false)));
    }

    [Fact]
    public void EquivalentProcessesCompareEveryOwnedValue()
    {
        NamespaceChannelModel channel = Channel("root", true);
        var first = new VProcessStateModel(1, null, "group", channel, channel);
        var same = new VProcessStateModel(1, null, "group", Channel("root", true), Channel("root", true));

        Assert.True(first.EquivalentTo(same));
        Assert.False(first.EquivalentTo(same with { ProcessId = 2 }));
        Assert.False(first.EquivalentTo(same with { ParentId = 4 }));
        Assert.False(first.EquivalentTo(same with { ProcessGroupId = "other" }));
        Assert.False(first.EquivalentTo(same with { Root = Channel("other", true) }));
        Assert.False(first.EquivalentTo(same with { CurrentDirectory = Channel("other", true) }));
    }

    [Fact]
    public void SnapshotAndChannelConversionsRoundTripStructurally()
    {
        NamespaceSnapshotModel snapshot = Snapshot(3, Head("one", 1), Head("two", 2));
        NamespaceChannelModel channel = Channel("root", true);

        Assert.True(snapshot.EquivalentTo(snapshot.ToDomain().ToModel()));
        Assert.True(channel.EquivalentTo(channel.ToDomain().ToModel()));
    }

    private static NamespaceSnapshotModel Snapshot(long nextMountId, params MountHeadModel[] heads)
        => new(nextMountId, heads);

    private static MountHeadModel Head(string device, ulong path)
    {
        ResourceHandleModel from = Handle($"from-{device}", path);
        var mount = new MountBindingModel(path.GetHashCode(), MountFlags.Replace, Handle(device, path + 20), device);
        return new MountHeadModel(from, new[] { mount });
    }

    private static NamespaceChannelModel Channel(string name, bool includeUnion)
    {
        ResourceHandleModel handle = Handle(name, 1);
        MountBindingModel[]? union = includeUnion
            ? new[] { new MountBindingModel(1, MountFlags.Before, handle, string.Empty) }
            : null;
        return new NamespaceChannelModel(new[] { new ChannelFrameModel(name, handle, handle, union) });
    }

    private static ResourceHandleModel Handle(string device, ulong path)
        => new(new ResourceIdentityModel("test", device, path), QidType.QTDIR, 0);
}

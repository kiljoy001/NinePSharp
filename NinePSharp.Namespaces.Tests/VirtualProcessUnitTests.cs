using NinePSharp.Constants;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class VirtualProcessUnitTests
{
    [Fact]
    public void ProcessAndGroupRejectInvalidConstruction()
    {
        NamespaceChannel root = Root();
        var group = new VProcessGroup(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new VProcessGroup(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VProcess(0, null, group, root, root));
        Assert.Throws<ArgumentNullException>(() => new VProcess(1, null, null!, root, root));
        Assert.Throws<ArgumentNullException>(() => new VProcess(1, null, group, null!, root));
        Assert.Throws<ArgumentNullException>(() => new VProcess(1, null, group, root, null!));
    }

    [Fact]
    public void ProcessTableValidatesLookupsForkModesAndInitialRoot()
    {
        var table = new VProcessTable();

        Assert.Throws<ArgumentNullException>(() => table.CreateInitial(null!));
        Assert.Throws<KeyNotFoundException>(() => table.Get(99));
        Assert.Throws<KeyNotFoundException>(() => table.Fork(99, NamespaceForkMode.Share));
        VProcess parent = table.CreateInitial(Root());
        Assert.Equal(1, parent.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Fork(parent.Id, (NamespaceForkMode)99));
        VProcess child = table.Fork(parent.Id, NamespaceForkMode.Share);
        Assert.Same(parent, table.Get(parent.Id));
        Assert.Same(child, table.Get(child.Id));

        VProcess restricted = table.Fork(parent.Id, NamespaceForkMode.Copy, noMounts: true);
        Assert.True(restricted.ProcessGroup.MountTable.MountsDisabled);
        Assert.False(parent.ProcessGroup.MountTable.MountsDisabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResourceIdentityRejectsInvalidProvider(string? provider)
        => Assert.ThrowsAny<ArgumentException>(() => new ResourceIdentity(provider!, "device", 1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResourceIdentityRejectsInvalidDevice(string? device)
        => Assert.ThrowsAny<ArgumentException>(() => new ResourceIdentity("provider", device!, 1));

    [Fact]
    public void ChangeDirectoryClonesTheChannelAndRejectsNull()
    {
        NamespaceChannel root = Root();
        var process = new VProcess(1, null, new VProcessGroup(1), root, root);
        NamespaceChannel directory = NamespaceChannel.Restore(
            new[]
            {
                root.Frames[0],
                new ChannelFrame("child", Directory("child", 2)),
            });

        process.ChangeDirectory(directory);

        Assert.NotSame(directory, process.CurrentDirectory);
        Assert.Equal(new[] { "child" }, process.CurrentDirectory.VisiblePath);
        Assert.Throws<ArgumentNullException>(() => process.ChangeDirectory(null!));
    }

    private static NamespaceChannel Root()
        => NamespaceChannel.Restore(new[] { new ChannelFrame("/", Directory("root", 1)) });

    private static ResourceHandle Directory(string device, ulong path)
        => new(new ResourceIdentity("test", device, path), QidType.QTDIR);
}

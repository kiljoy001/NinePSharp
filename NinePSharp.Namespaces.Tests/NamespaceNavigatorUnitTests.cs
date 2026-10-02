using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceNavigatorUnitTests
{
    [Fact]
    public async Task CreateAndEnterCreatedValidateArguments()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        var navigator = new NamespaceNavigator(new MountTable(), resources);
        NamespaceChannel channel = navigator.Attach(root);
        await Assert.ThrowsAsync<ArgumentNullException>(() => navigator.CreateAsync(null!, "file", false).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(navigator.EnterCreated(channel, "file", null!)));
    }

    [Fact]
    public void ConstructionAndAttachRejectNull()
    {
        var resources = new MemoryResources();
        var mounts = new MountTable();

        Assert.Throws<ArgumentNullException>(() => new NamespaceNavigator(null!, resources));
        Assert.Throws<ArgumentNullException>(() => new NamespaceNavigator(mounts, null!));
        Assert.Throws<ArgumentNullException>(() => new NamespaceNavigator(mounts, resources).Attach(null!));
        Assert.Throws<ArgumentNullException>(() => NamespaceChannel.Restore(null!));
        Assert.Throws<ArgumentException>(() => NamespaceChannel.Restore(Array.Empty<ChannelFrame>()));
    }

    [Fact]
    public async Task WalkHandlesRootParentDotEmptyPartialNullAndCancellation()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root", "child");
        var navigator = new NamespaceNavigator(new MountTable(), resources);
        NamespaceChannel channel = navigator.Attach(root);

        NamespaceWalkResult special = await navigator.WalkAsync(channel, new[] { "..", ".", string.Empty });
        Assert.True(special.Complete(3));
        Assert.Equal(root.Identity, special.Channel.Current.Identity);
        Assert.Empty(special.Channel.VisiblePath);

        NamespaceWalkResult partial = await navigator.WalkAsync(channel, new[] { "child", "missing" });
        Assert.False(partial.Complete(2));
        Assert.Single(partial.Qids);
        Assert.Equal(new[] { "child" }, partial.Channel.VisiblePath);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await navigator.WalkAsync(null!, Array.Empty<string>()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await navigator.WalkAsync(channel, null!));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await navigator.WalkAsync(channel, new string[] { null! }));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await navigator.WalkAsync(channel, new[] { "." }, canceled.Token));
    }

    [Fact]
    public void MountRejectsNullChannelsAndMountPoints()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        var navigator = new NamespaceNavigator(new MountTable(), resources);

        Assert.Throws<ArgumentNullException>(() => navigator.Mount(null!, root));
        Assert.Throws<ArgumentNullException>(() => navigator.Mount(navigator.Attach(root), null!));
    }

    [Fact]
    public async Task ReadAndCreateValidateArgumentsAndUseOrdinaryDirectories()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        var navigator = new NamespaceNavigator(new MountTable(), resources);
        NamespaceChannel channel = navigator.Attach(root);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await navigator.ReadDirectoryAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await navigator.CreateAsync(null!, " ", false));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await navigator.CreateAsync(channel, " ", false));

        ResourceHandle created = await navigator.CreateAsync(channel, "made", false);
        Assert.False(created.IsDirectory);
        Assert.True(resources.Contains(root, "made"));
    }

    [Fact]
    public async Task RestoredUnionWithoutMountAnchorCanCreateOrRejectCreation()
    {
        var resources = new MemoryResources();
        ResourceHandle first = resources.Directory("first");
        ResourceHandle second = resources.Directory("second");
        var navigator = new NamespaceNavigator(new MountTable(), resources);
        NamespaceChannel creatable = ManualUnion(first, second, true);
        NamespaceChannel readOnly = ManualUnion(first, second, false);

        await navigator.CreateAsync(creatable, "made", false);
        Assert.True(resources.Contains(second, "made"));
        NamespaceException failure = await Assert.ThrowsAsync<NamespaceException>(
            async () => await navigator.CreateAsync(readOnly, "denied", false));
        Assert.Equal(NamespaceError.CreateNotPermitted, failure.Error);
    }

    [Fact]
    public async Task DirectoryReadRewritesMountedEntryHandleButPreservesVisibleName()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle mountPoint = resources.AddChild(root, "visible", true);
        ResourceHandle replacement = resources.Directory("replacement");
        var mounts = new MountTable();
        mounts.Mount(replacement, mountPoint);
        var navigator = new NamespaceNavigator(mounts, resources);

        ResourceDirectoryEntry entry = Assert.Single(await navigator.ReadDirectoryAsync(navigator.Attach(root)));
        Assert.Equal("visible", entry.Name);
        Assert.Equal(replacement.Identity, entry.Handle.Identity);
    }

    private static NamespaceChannel ManualUnion(ResourceHandle first, ResourceHandle second, bool create)
    {
        var union = new[]
        {
            new MountBinding(1, MountFlags.Before, first, string.Empty),
            new MountBinding(2, create ? MountFlags.After | MountFlags.Create : MountFlags.After, second, string.Empty),
        };
        return NamespaceChannel.Restore(new[] { new ChannelFrame("/", first, null, union) });
    }
}

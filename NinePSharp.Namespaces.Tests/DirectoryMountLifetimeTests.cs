using System.Reflection;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DirectoryMountLifetimeTests
{
    [Fact]
    public async Task ClosingNamespaceEmptiesRetainedHeadsReleasesGateAndDropsTableOwnership()
    {
        await using var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A"), f.Directory("B"));
        MountTable table = f.Mounts;
        ResourceIdentity root = f.Root.Identity;
        DirectoryMountHead head = table.RetainDirectoryHead(root)!;
        await head.Gate.WaitAsync();
        table.Close();
        Assert.Equal(2, head.Members.Count);
        head.Gate.Release();

        // RetireAsync was queued before this gate acquisition.
        await head.Gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(head.Members);
        head.Gate.Release();
        var retained = (System.Collections.IDictionary)typeof(MountTable)
            .GetField("directoryHeads", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!;
        Assert.Empty(retained);
        Assert.Throws<NamespaceException>(() => table.RetainDirectoryHead(root));
    }

    [Fact]
    public async Task RemovingTheLastSelectedMemberEmptiesTheOldRetainedHead()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A");
        var b = f.Directory("B");
        f.Union(a, b);
        var head = f.Mounts.RetainDirectoryHead(f.Root.Identity)!;
        await f.Mounts.UnmountAsync(f.Root, b);
        Assert.Single(head.Members);
        await f.Mounts.UnmountAsync(f.Root, a);
        Assert.Empty(head.Members);
        Assert.Null(f.Mounts.Find(f.Root.Identity));
    }

    [Fact]
    public async Task AsyncMountValidationMatchesSynchronousEntryPoints()
    {
        await using var f = new StreamingDirectoryFixture();
        var root = f.Root;
        var file = new ResourceHandle(new("memory-data", "root", 100), 0);
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) });
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Mounts.MountAsync((ResourceHandle)null!, root).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Mounts.MountAsync(root, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Mounts.MountAsync((NamespaceChannel)null!, root).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Mounts.MountAsync(channel, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Mounts.UnmountAsync(null!).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Mounts.MountAsync(channel, root, MountFlags.Cache).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Mounts.MountAsync(file, file).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Mounts.MountAsync(file, file, MountFlags.After).AsTask());
        f.Mounts.SetMountsDisabled(true);
        await Assert.ThrowsAsync<NamespaceException>(() => f.Mounts.MountAsync(root, root).AsTask());
    }

    [Fact]
    public async Task AsyncBindUsesCurrentSourceMountsAndHandlesAnExplicitEmptyUnion()
    {
        await using var f = new StreamingDirectoryFixture();
        var a = f.Directory("A");
        var b = f.Directory("B");
        var c = f.Directory("C");
        var x = f.Directory("X");
        f.Union(a, b);
        var source = await f.Files.Local.AttachAsync(f.Root, default);
        f.Mounts.Mount(c, f.Root);
        await f.Mounts.MountAsync(source, x);
        Assert.Equal(c, f.Mounts.Find(x.Identity)!.Mounts[0].Target);
        var empty = NamespaceChannel.Restore(new[] { new ChannelFrame("/", a, Union: Array.Empty<MountBinding>()) });
        await f.Mounts.MountAsync(empty, x);
        Assert.Equal(a, f.Mounts.Find(x.Identity)!.Mounts[0].Target);
    }
}

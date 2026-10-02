using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Namespace fids survive unmount")]
public sealed class FidUnmountSteps
{
    private readonly MemoryDataResources resources = new();
    private readonly MountTable mounts = new();
    private NamespaceSession session = null!;
    private ResourceHandle root = null!;
    private ResourceHandle mountPoint = null!;
    private ResourceHandle mounted = null!;
    private ResourceHandle originalFile = null!;
    private ResourceHandle underlyingFile = null!;
    private ResourceHandle replacementFile = null!;
    private bool opened;
    private bool disconnected;

    [Given("a mounted file selected by an (.*) fid")]
    public async Task GivenMountedFile(string state)
    {
        root = resources.Directory("root");
        mountPoint = await resources.CreateAsync(root, "mnt", true, CancellationToken.None);
        underlyingFile = await resources.CreateAsync(mountPoint, "file", false, CancellationToken.None);
        mounted = resources.Directory("mounted", "file");

        // A union makes selected removal distinguishable from complete removal.
        mounts.Mount(mounted, mountPoint, MountFlags.Before);
        session = new NamespaceSession("unmount", 1, "glenda", new LocalNamespaceDataPlane(mounts, resources));
        await session.AttachAsync(1, root);
        Assert.True((await session.WalkAsync(1, 3, new[] { "mnt", "file" })).Complete(2));
        originalFile = session.GetFidResource(3);
        Assert.Equal(mounted.Identity.Device, originalFile.Identity.Device);
        Assert.NotEqual(underlyingFile.Identity, originalFile.Identity);

        // Seed through a separate open instance so fid 3 can remain unopened.
        await session.WalkAsync(3, 4, Array.Empty<string>());
        await session.OpenAsync(4, NinePConstants.ORDWR);
        await session.WriteAsync(4, 0, Encoding.UTF8.GetBytes("original"));
        await session.ClunkAsync(4);
        opened = state == "open";
        Assert.Contains(state, new[] { "open", "unopened" });
        if (opened)
        {
            await session.OpenAsync(3, NinePConstants.ORDWR);
        }
    }

    [When("the mount is (.*)")]
    public async Task ChangeMount(string change)
    {
        if (change == "selectively unmounted")
        {
            mounts.Unmount(mountPoint, mounted);
            Assert.Equal(mountPoint.Identity, Assert.Single(mounts.Find(mountPoint.Identity)!.Mounts).Target.Identity);
        }
        else
        {
            Assert.Contains(change, new[] { "completely unmounted", "unmounted and replaced" });
            mounts.Unmount(mountPoint);
            Assert.Null(mounts.Find(mountPoint.Identity));
        }

        if (change == "unmounted and replaced")
        {
            ResourceHandle replacement = resources.Directory("replacement", "file");
            replacementFile = (await resources.WalkAsync(replacement, "file", CancellationToken.None))!;
            mounts.Mount(replacement, mountPoint, MountFlags.Replace);
        }
    }

    [Then("the retained fid still reads and writes the original file")]
    public async Task RetainedFileIsUsable()
    {
        Assert.Equal(originalFile.Identity, session.GetFidResource(3).Identity);
        if (!opened)
        {
            await session.OpenAsync(3, NinePConstants.ORDWR);
        }

        Assert.Equal("original", Encoding.UTF8.GetString((await session.ReadAsync(3, 0, 100)).Span));
        Assert.Equal(8U, await session.WriteAsync(3, 0, Encoding.UTF8.GetBytes("retained")));
        Assert.Equal("retained", Encoding.UTF8.GetString((await session.ReadAsync(3, 0, 100)).Span));
        Assert.Equal(originalFile.Identity, (await session.StatAsync(3)).Resource.Identity);
    }

    [Then("a fresh walk selects the (.*) file")]
    public async Task FreshWalkUsesCurrentNamespace(string visible)
    {
        Assert.Contains(visible, new[] { "underlying", "replacement" });
        Assert.True((await session.WalkAsync(1, 5, new[] { "mnt", "file" })).Complete(2));
        ResourceHandle expected = visible == "underlying" ? underlyingFile : replacementFile;
        Assert.Equal(expected.Identity, session.GetFidResource(5).Identity);
        Assert.NotEqual(originalFile.Identity, expected.Identity);
        Assert.Equal(0UL, (await session.StatAsync(5)).Length);
    }

    [Then("unmount has not clunked the retained file handle")]
    public void RetainedHandleHasNotBeenClunked() => Assert.Equal(1, resources.ClunkCount);

    [When("the retained fid is released by (.*)")]
    public async Task ReleaseFid(string cleanup)
    {
        Assert.Contains(cleanup, new[] { "clunk", "disconnect" });
        disconnected = cleanup == "disconnect";
        if (disconnected)
        {
            await session.DisposeAsync();
        }
        else
        {
            await session.ClunkAsync(3);
        }
    }

    [Then("its provider handle is clunked once and the fid is invalid")]
    public async Task FidHasBeenReleased()
    {
        Assert.Equal(2, resources.ClunkCount);
        Assert.False(session.ContainsFid(3));
        if (disconnected)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadAsync(3, 0, 1).AsTask());
            await session.DisposeAsync();
        }
        else
        {
            await Assert.ThrowsAsync<NamespaceFidException>(() => session.ReadAsync(3, 0, 1).AsTask());
            await Assert.ThrowsAsync<NamespaceFidException>(() => session.ClunkAsync(3).AsTask());
            await session.AttachAsync(3, root);
            Assert.Equal(root.Identity, session.GetFidResource(3).Identity);
        }

        Assert.Equal(2, resources.ClunkCount);
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }
}

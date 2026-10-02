using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class ResourceDataContractTests
{
    [Fact]
    public void OperationIdValidatesAndRetainsValues()
    {
        Assert.Throws<ArgumentException>(() => new ResourceOperationId(string.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceOperationId("session", 0));

        var id = new ResourceOperationId("session", 42);
        Assert.Equal("session", id.SessionId);
        Assert.Equal(42UL, id.Sequence);
    }

    [Fact]
    public void OperationContextValidatesAndRetainsValues()
    {
        var id = new ResourceOperationId("session", 1);
        Assert.Throws<ArgumentNullException>(() => new ResourceOperationContext(null!, 7, "glenda"));
        Assert.Throws<ArgumentException>(() => new ResourceOperationContext(id, 7, string.Empty));

        var context = new ResourceOperationContext(id, 7, "glenda");
        Assert.Same(id, context.OperationId);
        Assert.Equal(7, context.ProcessId);
        Assert.Equal("glenda", context.User);
    }

    [Fact]
    public void OpenHandleValidatesAndRetainsValues()
    {
        var resource = new ResourceHandle(new ResourceIdentity("provider", "device", 1), QidType.QTFILE);
        Assert.Throws<ArgumentNullException>(
            () => new ResourceOpenHandle(null!, "handle", NinePConstants.OREAD, 0));
        Assert.Throws<ArgumentException>(
            () => new ResourceOpenHandle(resource, string.Empty, NinePConstants.OREAD, 0));

        var handle = new ResourceOpenHandle(resource, "handle", NinePConstants.ORDWR, 4096);
        Assert.Same(resource, handle.Resource);
        Assert.Equal("handle", handle.HandleId);
        Assert.Equal(NinePConstants.ORDWR, handle.Mode);
        Assert.Equal(4096U, handle.IoUnit);
    }

    [Fact]
    public void LocalDataPlaneValidatesDependencies()
    {
        var resources = new MemoryDataResources();
        Assert.Throws<ArgumentNullException>(() => new LocalNamespaceDataPlane(null!, resources));
        Assert.Throws<ArgumentNullException>(() => new LocalNamespaceDataPlane(new MountTable(), null!));
    }

    [Fact]
    public async Task LocalDataPlaneReportsVisibleRootAndChildNames()
    {
        var resources = new MemoryDataResources();
        ResourceHandle root = resources.Directory("names", "child");
        var plane = new LocalNamespaceDataPlane(new MountTable(), resources);
        NamespaceChannel rootChannel = await plane.AttachAsync(root, CancellationToken.None);
        NamespaceWalkResult child = await plane.WalkAsync(rootChannel, new[] { "child" }, CancellationToken.None);

        Assert.Equal("/", (await plane.StatAsync(rootChannel, CancellationToken.None)).Name);
        Assert.Equal("child", (await plane.StatAsync(child.Channel, CancellationToken.None)).Name);
        Assert.Equal("child", Assert.Single(await plane.ReadDirectoryAsync(rootChannel, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task NavigatorValidatesCreateAndCreatedChannelArguments()
    {
        var resources = new MemoryDataResources();
        ResourceHandle root = resources.Directory("arguments");
        var navigator = new NamespaceNavigator(new MountTable(), resources);
        NamespaceChannel channel = navigator.Attach(root);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => navigator.CreateAsync(null!, "child", false).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => navigator.CreateAsync(channel, string.Empty, false).AsTask());
        Assert.Throws<ArgumentNullException>(() => navigator.SelectCreateTarget(null!));
        Assert.Throws<ArgumentNullException>(() => navigator.EnterCreated(null!, "child", root));
        Assert.Throws<ArgumentException>(() => navigator.EnterCreated(channel, string.Empty, root));
        Assert.Throws<ArgumentNullException>(() => navigator.EnterCreated(channel, "child", null!));
    }
}

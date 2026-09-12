using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceSyscallsTests
{
    [Fact]
    public async Task BindResolvesPathsAndChangesTheProcessNamespace()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle source = resources.AddChild(root, "source", true);
        ResourceHandle target = resources.AddChild(root, "target", true);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var syscalls = new NamespaceSyscalls(resources);

        MountBinding binding = await syscalls.BindAsync(process, "/source", "/target");

        Assert.Equal(source.Identity, binding.Target.Identity);
        Assert.Equal(source.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);
    }

    [Fact]
    public async Task MountRequiresReadWriteAuthenticatedServiceAndClosesDescriptor()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle target = resources.AddChild(root, "target", true);
        ResourceHandle service = resources.Directory("service", "child");
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var syscalls = new NamespaceSyscalls(resources);
        bool closed = false;
        var source = new NamespaceMountSource(
            new NamespaceNavigator(new MountTable(), resources).Attach(service),
            NinePConstants.ORDWR,
            "named-tree",
            authenticated: true,
            requiresAuthentication: true,
            closeAsync: () =>
            {
                closed = true;
                return ValueTask.CompletedTask;
            });

        MountBinding binding = await syscalls.MountAsync(process, source, "/target", MountFlags.Cache);

        Assert.Equal("named-tree", binding.Spec);
        Assert.True(closed);
        Assert.Equal(service.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);
    }

    [Fact]
    public async Task MountRejectsReadOnlyAndUnauthenticatedSourcesWithoutMutation()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle target = resources.AddChild(root, "target", true);
        ResourceHandle service = resources.Directory("service");
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var syscalls = new NamespaceSyscalls(resources);
        NamespaceChannel serviceChannel = new NamespaceNavigator(new MountTable(), resources).Attach(service);

        NamespaceException modeFailure = await Assert.ThrowsAsync<NamespaceException>(async () =>
            await syscalls.MountAsync(process, new NamespaceMountSource(serviceChannel, NinePConstants.OREAD), "/target"));
        NamespaceException authFailure = await Assert.ThrowsAsync<NamespaceException>(async () =>
            await syscalls.MountAsync(
                process,
                new NamespaceMountSource(serviceChannel, NinePConstants.ORDWR, authenticated: false, requiresAuthentication: true),
                "/target"));

        Assert.Equal(NamespaceError.MountSourceNotReadWrite, modeFailure.Error);
        Assert.Equal(NamespaceError.MountAuthenticationRequired, authFailure.Error);
        Assert.Null(process.ProcessGroup.MountTable.Find(target.Identity));
    }

    [Fact]
    public async Task UnmountResolvesTargetAndOptionalSourcePaths()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle source = resources.AddChild(root, "source", true);
        ResourceHandle target = resources.AddChild(root, "target", true);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ProcessGroup.MountTable.Mount(source, target);
        var syscalls = new NamespaceSyscalls(resources);

        await syscalls.UnmountAsync(process, "/target", "/source");

        Assert.Null(process.ProcessGroup.MountTable.Find(target.Identity));
    }
}

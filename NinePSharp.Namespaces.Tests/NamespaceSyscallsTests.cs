using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceSyscallsTests
{
    [Fact]
    public void ConstructorsRejectNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new NamespaceMountSource(null!, NinePConstants.ORDWR));
        Assert.Throws<ArgumentNullException>(() => new NamespaceSyscalls(null!));
    }

    [Fact]
    public async Task SyscallsRejectNullProcessesAndSources()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle service = resources.Directory("service");
        var syscalls = new NamespaceSyscalls(resources);
        NamespaceMountSource source = new(
            new NamespaceNavigator(new MountTable(), resources).Attach(service),
            NinePConstants.ORDWR);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            syscalls.BindAsync(null!, "/source", "/target").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            syscalls.MountAsync(null!, source, "/target").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            syscalls.MountAsync(
                new VProcessTable().CreateInitial(
                    new NamespaceNavigator(new MountTable(), resources).Attach(root)),
                null!,
                "/target").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            syscalls.UnmountAsync(null!, "/target").AsTask());
    }

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

    [Fact]
    public async Task UnmountWithoutSourceRemovesEveryMember()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle first = resources.AddChild(root, "first", true);
        ResourceHandle second = resources.AddChild(root, "second", true);
        ResourceHandle target = resources.AddChild(root, "target", true);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ProcessGroup.MountTable.Mount(first, target, MountFlags.Before);
        process.ProcessGroup.MountTable.Mount(second, target, MountFlags.After);

        await new NamespaceSyscalls(resources).UnmountAsync(process, "/target");

        Assert.Null(process.ProcessGroup.MountTable.Find(target.Identity));
    }

    [Fact]
    public async Task RelativePathsResolveFromTheCurrentDirectory()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle working = resources.AddChild(root, "working", true);
        ResourceHandle source = resources.AddChild(working, "source", true);
        ResourceHandle target = resources.AddChild(working, "target", true);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        NamespaceWalkResult cwd = await new NamespaceNavigator(new MountTable(), resources)
            .WalkAsync(process.Root, new[] { "working" });
        process.ChangeDirectory(cwd.Channel);

        await new NamespaceSyscalls(resources).BindAsync(process, "source", "target");

        Assert.Equal(source.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);
    }

    [Fact]
    public async Task MountLeavesAnExistingTargetMountHiddenWithoutCrossingIt()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle target = resources.AddChild(root, "target", true);
        ResourceHandle hidden = resources.Directory("hidden");
        ResourceHandle service = resources.Directory("service");
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ProcessGroup.MountTable.Mount(hidden, target);
        NamespaceMountSource source = new(
            new NamespaceNavigator(new MountTable(), resources).Attach(service),
            NinePConstants.ORDWR);

        await new NamespaceSyscalls(resources).MountAsync(process, source, "/target");

        Assert.Equal(service.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);
    }
}

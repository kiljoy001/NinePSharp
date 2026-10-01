using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceSyscallUnionTests
{
    [Theory]
    [InlineData("/union/target")]
    [InlineData("/union/./target")]
    [InlineData("//union//target//.")]
    public async Task BindFindsTargetInLaterUnionMemberWithoutCrossingItsMount(string path)
    {
        var fixture = new UnionFixture();
        MountBinding binding = await fixture.Syscalls.BindAsync(fixture.Process, "/source", path);

        Assert.Equal(fixture.Source.Identity, binding.Target.Identity);
        Assert.Equal(fixture.Source.Identity, fixture.MountedTarget());
        Assert.Null(fixture.Mounts.Find(fixture.Hidden.Identity));
        Assert.Equal(fixture.First.Identity, fixture.Mounts.Find(fixture.Union.Identity)!.Mounts[0].Target.Identity);
        Assert.Empty(fixture.Process.Root.VisiblePath);
    }

    [Fact]
    public async Task ServiceMountFindsUnionTargetAndClosesSourceOnlyAfterSuccess()
    {
        var fixture = new UnionFixture();
        int closes = 0;
        var source = new NamespaceMountSource(
            new NamespaceNavigator(new MountTable(), fixture.Resources).Attach(fixture.Source),
            NinePConstants.ORDWR,
            "service-tree",
            closeAsync: () =>
            {
                closes++;
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAsync<NamespaceException>(() =>
            fixture.Syscalls.MountAsync(fixture.Process, source, "/union/missing").AsTask());
        Assert.Equal(0, closes);
        Assert.Equal(fixture.Hidden.Identity, fixture.MountedTarget());

        MountBinding binding = await fixture.Syscalls.MountAsync(fixture.Process, source, "/union/target");
        Assert.Equal(1, closes);
        Assert.Equal("service-tree", binding.Spec);
        Assert.Equal(fixture.Source.Identity, fixture.MountedTarget());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/hidden")]
    public async Task UnmountFindsUnderlyingTargetInLaterUnionMember(string? source)
    {
        var fixture = new UnionFixture();
        await fixture.Syscalls.UnmountAsync(fixture.Process, "/union/target", source);

        Assert.Null(fixture.Mounts.Find(fixture.Target.Identity));
        Assert.NotNull(fixture.Mounts.Find(fixture.Union.Identity));
    }

    [Fact]
    public async Task FirstMatchingUnionMemberWins()
    {
        var fixture = new UnionFixture();
        ResourceHandle firstTarget = fixture.Resources.AddChild(fixture.First, "target", true);

        await fixture.Syscalls.BindAsync(fixture.Process, "/source", "/union/target");

        Assert.Equal(fixture.Source.Identity, fixture.Mounts.Find(firstTarget.Identity)!.Mounts[0].Target.Identity);
        Assert.Equal(fixture.Hidden.Identity, fixture.MountedTarget());
    }

    [Fact]
    public async Task RelativeTargetRetainsCurrentDirectoryUnionFallback()
    {
        var fixture = new UnionFixture();
        var navigator = new NamespaceNavigator(fixture.Mounts, fixture.Resources);
        NamespaceWalkResult cwd = await navigator.WalkAsync(fixture.Process.Root, new[] { "union" });
        fixture.Process.ChangeDirectory(cwd.Channel);

        await fixture.Syscalls.BindAsync(fixture.Process, "/source", "target");

        Assert.Equal(fixture.Source.Identity, fixture.MountedTarget());
        Assert.Equal(new[] { "union" }, fixture.Process.CurrentDirectory.VisiblePath);
        Assert.Equal(fixture.First.Identity, fixture.Process.CurrentDirectory.Current.Identity);
    }

    [Fact]
    public async Task MissingIntermediateComponentCannotChangeMounts()
    {
        var fixture = new UnionFixture();
        NamespaceException failure = await Assert.ThrowsAsync<NamespaceException>(() =>
            fixture.Syscalls.BindAsync(fixture.Process, "/source", "/union/missing/target").AsTask());

        Assert.Equal(NamespaceError.ResourceNotFound, failure.Error);
        Assert.Equal(fixture.Hidden.Identity, fixture.MountedTarget());
    }

    [Property(MaxTest = 100)]
    public async Task<bool> BindAndUnmountReachAnyUnionMember(NonNegativeInt prefixCount)
    {
        var fixture = new UnionFixture();
        for (int index = 0; index < prefixCount.Get % 12; index++)
        {
            fixture.Mounts.Mount(fixture.Resources.Directory($"prefix-{index}"), fixture.Union, MountFlags.Before);
        }

        await fixture.Syscalls.BindAsync(fixture.Process, "/source", "/union/target");
        Assert.Equal(fixture.Source.Identity, fixture.MountedTarget());
        await fixture.Syscalls.UnmountAsync(fixture.Process, "/union/target", "/source");
        return fixture.Mounts.Find(fixture.Target.Identity) is null;
    }

    [Theory]
    [InlineData("/file/", "/target", true)]
    [InlineData("/file/.", "/target", true)]
    [InlineData("/file", "/target/", true)]
    [InlineData("/file", "/target/.", true)]
    [InlineData("/file", "/target", false)]
    public async Task TrailingDirectorySyntaxRejectsFilesBeforeMutation(string sourcePath, string targetPath, bool rejected)
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle source = resources.AddChild(root, "file", false);
        ResourceHandle target = resources.AddChild(root, "target", false);
        var process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var syscalls = new NamespaceSyscalls(resources);

        if (rejected)
        {
            NamespaceException failure = await Assert.ThrowsAsync<NamespaceException>(() =>
                syscalls.BindAsync(process, sourcePath, targetPath).AsTask());
            Assert.Equal(NamespaceError.ResourceNotDirectory, failure.Error);
            Assert.Null(process.ProcessGroup.MountTable.Find(target.Identity));
        }
        else
        {
            await syscalls.BindAsync(process, sourcePath, targetPath);
            Assert.Equal(source.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);
        }
    }

    private sealed class UnionFixture
    {
        internal UnionFixture()
        {
            ResourceHandle root = Resources.Directory("root");
            Source = Resources.AddChild(root, "source", true);
            Union = Resources.AddChild(root, "union", true);
            Target = Resources.AddChild(Union, "target", true);
            Hidden = Resources.AddChild(root, "hidden", true);
            First = Resources.Directory("first");
            Process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), Resources).Attach(root));
            Mounts.Mount(First, Union, MountFlags.Before);
            Mounts.Mount(Hidden, Target);
            Syscalls = new NamespaceSyscalls(Resources);
        }

        internal MemoryResources Resources { get; } = new();

        internal VProcess Process { get; }

        internal MountTable Mounts => Process.ProcessGroup.MountTable;

        internal NamespaceSyscalls Syscalls { get; }

        internal ResourceHandle Source { get; }

        internal ResourceHandle Union { get; }

        internal ResourceHandle Target { get; }

        internal ResourceHandle Hidden { get; }

        internal ResourceHandle First { get; }

        internal ResourceIdentity MountedTarget() => Mounts.Find(Target.Identity)!.Mounts[0].Target.Identity;
    }
}

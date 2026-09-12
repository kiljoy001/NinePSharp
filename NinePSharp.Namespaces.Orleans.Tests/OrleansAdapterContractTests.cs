using Moq;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Orleans;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class OrleansAdapterContractTests
{
    [Fact]
    public void NullAdapterDependenciesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new OrleansResourceOperations(null!));
        Assert.Throws<ArgumentNullException>(() => new DistributedNamespaceOperations(null!, new Mock<IResourceOperations>().Object));
        Assert.Throws<ArgumentNullException>(() => new DistributedNamespaceOperations(new Mock<IGrainFactory>().Object, null!));
    }

    [Theory]
    [InlineData("walk")]
    [InlineData("directory")]
    [InlineData("create")]
    [InlineData("open")]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("stat")]
    [InlineData("create-open")]
    [InlineData("clunk")]
    [InlineData("remove")]
    public async Task AlreadyCancelledCallsNeverReachAResourceGrain(string operation)
    {
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        var adapter = new OrleansResourceOperations(resolver.Object);
        var token = new CancellationToken(true);
        var root = GatewayTestContext.Root;
        var handle = new ResourceOpenHandle(root, "open", 0, 0);
        var context = new ResourceOperationContext(new ResourceOperationId("unit", 1), 1, "user");
        Func<Task> call = operation switch
        {
            "walk" => () => adapter.WalkAsync(root, "file", token).AsTask(),
            "directory" => () => adapter.ReadDirectoryAsync(root, token).AsTask(),
            "create" => () => adapter.CreateAsync(root, "file", false, token).AsTask(),
            "open" => () => adapter.OpenAsync(root, 0, context, token).AsTask(),
            "read" => () => adapter.ReadAsync(handle, 0, 1, token).AsTask(),
            "write" => () => adapter.WriteAsync(handle, 0, new byte[1], context, token).AsTask(),
            "stat" => () => adapter.StatAsync(root, token).AsTask(),
            "create-open" => () => adapter.CreateAndOpenAsync(root, "file", 0, 0, context, token).AsTask(),
            "clunk" => () => adapter.ClunkAsync(handle, context, token).AsTask(),
            "remove" => () => adapter.RemoveAsync(root, handle, context, token).AsTask(),
            _ => throw new InvalidOperationException(),
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(call);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DistributedOperationsValidateChannelsGroupsAndDataSupport()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var operations = new DistributedNamespaceOperations(factory.Object, new Mock<IResourceOperations>().Object);
        var context = new ResourceOperationContext(new ResourceOperationId("unit", 1), 1, "user");
        await Assert.ThrowsAsync<ArgumentNullException>(() => operations.OpenAsync(null!, 0, context));
        await Assert.ThrowsAsync<ArgumentNullException>(() => operations.RemoveAsync(null!, null, context));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.AttachAsync(" ", GatewayTestContext.Root));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.AttachAsync("group", GatewayTestContext.Root, new CancellationToken(true)));
        factory.VerifyNoOtherCalls();
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", GatewayTestContext.Root) });
        await Assert.ThrowsAsync<NotSupportedException>(() => operations.OpenAsync(channel, 0, context));
    }

    [Fact]
    public async Task RootStatNameIsSlashEvenWhenRestoredProviderFrameHasAnotherName()
    {
        var test = new GatewayTestContext();
        var operations = new DistributedNamespaceOperations(test.Factory.Object, test.Resources.Object);
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("provider-root", GatewayTestContext.Root) });
        Assert.Equal("/", (await operations.StatAsync("group", channel)).Name);
    }

    [Fact]
    public async Task DistributedMountAndUnmountDelegateToTheProcessGroup()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var group = new Mock<IVProcessGroupGrain>(MockBehavior.Strict);
        factory.Setup(value => value.GetGrain<IVProcessGroupGrain>("group", null)).Returns(group.Object);
        ResourceHandle target = GatewayTestContext.Root;
        ResourceHandle mountedOn = new(new ResourceIdentity("test", "resource", 3), QidType.QTDIR);
        MountBindingModel binding = new(7, MountFlags.Replace, target.ToModel(), "service");
        group.Setup(value => value.MountAsync(target.ToModel(), mountedOn.ToModel(), MountFlags.Replace, "service"))
            .ReturnsAsync(binding);
        group.Setup(value => value.UnmountAsync(mountedOn.ToModel(), null)).Returns(Task.CompletedTask);
        var operations = new DistributedNamespaceOperations(factory.Object, new Mock<IResourceOperations>().Object);

        MountBinding result = await operations.MountAsync("group", target, mountedOn, MountFlags.Replace, "service");
        await operations.UnmountAsync("group", mountedOn);

        Assert.Equal(7, result.MountId);
        group.VerifyAll();
    }

    [Fact]
    public async Task AlreadyCancelledNamespaceMutationsDoNotReachTheProcessGroup()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var operations = new DistributedNamespaceOperations(factory.Object, new Mock<IResourceOperations>().Object);
        ResourceHandle root = GatewayTestContext.Root;
        var cancelled = new CancellationToken(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.MountAsync("group", root, root, cancellationToken: cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.UnmountAsync("group", root, cancellationToken: cancelled));
        factory.VerifyNoOtherCalls();
    }
}

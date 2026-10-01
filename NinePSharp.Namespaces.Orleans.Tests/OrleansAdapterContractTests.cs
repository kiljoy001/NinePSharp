using Moq;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Orleans;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class OrleansAdapterContractTests
{
    [Fact]
    public async Task NativeOpenStatPassesTheExactHandleAndPreservesErrors()
    {
        var grain = new Mock<IOpenStatResourceGrain>(MockBehavior.Strict);
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        var handle = new ResourceOpenHandle(GatewayTestContext.File, "retained-open", 1, 99);
        resolver.Setup(r => r.Resolve(handle.Resource.Identity.ToModel())).Returns(grain.Object);
        var expected = new ResourceStat(handle.Resource, "provider-name", 42, 11, 12, 51, "u", "g", "m");
        grain.Setup(g => g.StatOpenAsync(handle.ToModel())).ReturnsAsync(expected.ToModel());
        var adapter = new OrleansResourceOperations(resolver.Object);
        Assert.Equal(expected, await adapter.StatOpenAsync(handle, default));
        var failure = new IOException("provider rejected handle");
        grain.Setup(g => g.StatOpenAsync(handle.ToModel())).ThrowsAsync(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => adapter.StatOpenAsync(handle, default).AsTask()));
        grain.Verify(g => g.StatOpenAsync(handle.ToModel()), Times.Exactly(2));
        grain.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NativeOpenStatRejectsMissingCapabilityAndCancelledAdmission()
    {
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        var handle = new ResourceOpenHandle(GatewayTestContext.File, "retained", 0, 0);
        resolver.Setup(r => r.Resolve(handle.Resource.Identity.ToModel())).Returns(new Mock<IMountableResourceGrain>().Object);
        var adapter = new OrleansResourceOperations(resolver.Object);
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.StatOpenAsync(handle, default).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.StatOpenAsync(handle, cancellation.Token).AsTask());
        resolver.Verify(r => r.Resolve(It.IsAny<ResourceIdentityModel>()), Times.Once);
    }

    [Fact]
    public async Task NativeWstatForwardsExactSentinelsContextAndRetainedHandle()
    {
        var grain = new Mock<IWStatResourceGrain>(MockBehavior.Strict);
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        var handle = new ResourceOpenHandle(GatewayTestContext.File, "retained-open", 1, 99, true);
        var context = new ResourceOperationContext(new("wstat", 1), 7, "glenda");
        ResourceWStat update = ResourceWStat.Unchanged(57) with { Name = "new", Mode = NinePConstants.Mode0600 };
        resolver.Setup(r => r.Resolve(handle.Resource.Identity.ToModel())).Returns(grain.Object);
        grain.Setup(g => g.WStatAsync(handle.Resource.ToModel(), update.ToModel(), context.ToModel())).ReturnsAsync(57U);
        grain.Setup(g => g.WStatOpenAsync(handle.ToModel(), update.ToModel(), context.ToModel())).ReturnsAsync(23U);
        var adapter = new OrleansResourceOperations(resolver.Object);
        Assert.Equal(57U, await adapter.WStatAsync(handle.Resource, update, context, default));
        Assert.Equal(23U, await adapter.WStatOpenAsync(handle, update, context, default));
        Assert.True(handle.ToModel().IsMountTransport);
        grain.VerifyAll();
    }

    [Fact]
    public async Task NativeWstatRejectsMissingCapabilityAndCancelledAdmission()
    {
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        var resource = GatewayTestContext.File;
        var handle = new ResourceOpenHandle(resource, "retained", 0, 0);
        var context = new ResourceOperationContext(new("wstat", 1), 1, "user");
        resolver.Setup(r => r.Resolve(resource.Identity.ToModel())).Returns(new Mock<IMountableResourceGrain>().Object);
        var adapter = new OrleansResourceOperations(resolver.Object);
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.WStatAsync(resource, ResourceWStat.Unchanged(), context, default).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.WStatOpenAsync(handle, ResourceWStat.Unchanged(), context, default).AsTask());
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.WStatAsync(resource, ResourceWStat.Unchanged(), context, cancelled).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.WStatOpenAsync(handle, ResourceWStat.Unchanged(), context, cancelled).AsTask());
        resolver.Verify(r => r.Resolve(It.IsAny<ResourceIdentityModel>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWstatTranslatesOnlyDefiniteProviderRejections(bool retained)
    {
        var grain = new Mock<IWStatResourceGrain>(MockBehavior.Strict);
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        ResourceHandle resource = GatewayTestContext.File;
        var handle = new ResourceOpenHandle(resource, "retained", 0, 0);
        var context = new ResourceOperationContext(new("wstat-rejection", 1), 1, "user");
        ResourceWStat update = ResourceWStat.Unchanged();
        resolver.Setup(r => r.Resolve(resource.Identity.ToModel())).Returns(grain.Object);
        grain.Setup(g => g.WStatAsync(resource.ToModel(), update.ToModel(), context.ToModel()))
            .ThrowsAsync(new ResourceWStatRejectedGrainException("denied"));
        grain.Setup(g => g.WStatOpenAsync(handle.ToModel(), update.ToModel(), context.ToModel()))
            .ThrowsAsync(new ResourceWStatRejectedGrainException("denied"));
        var adapter = new OrleansResourceOperations(resolver.Object);
        Func<Task> invoke = retained
            ? () => adapter.WStatOpenAsync(handle, update, context, default).AsTask()
            : () => adapter.WStatAsync(resource, update, context, default).AsTask();
        Assert.Equal("denied", (await Assert.ThrowsAsync<ResourceWStatRejectedException>(invoke)).Message);

        var uncertain = new IOException("reply lost");
        if (retained)
            grain.Setup(g => g.WStatOpenAsync(handle.ToModel(), update.ToModel(), context.ToModel())).ThrowsAsync(uncertain);
        else
            grain.Setup(g => g.WStatAsync(resource.ToModel(), update.ToModel(), context.ToModel())).ThrowsAsync(uncertain);
        Assert.Same(uncertain, await Assert.ThrowsAsync<IOException>(invoke));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectoryRejectionsAreTranslatedButTransportFailuresRemainUncertain(bool open)
    {
        var grain = new Mock<IMountableResourceGrain>(MockBehavior.Strict);
        var resolver = new Mock<IMountableResourceResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.Resolve(It.IsAny<ResourceIdentityModel>())).Returns(grain.Object);
        var adapter = new OrleansResourceOperations(resolver.Object);
        var resource = GatewayTestContext.Root;
        var handle = new ResourceOpenHandle(resource, "directory", 0, 0);
        var context = new ResourceOperationContext(new("directory", 1), 1, "user");
        Exception failure = new ResourceDirectoryRejectedGrainException("denied");
        grain.Setup(g => g.OpenAsync(It.IsAny<ResourceHandleModel>(), 0, It.IsAny<ResourceOperationContextModel>()))
            .Returns(() => Task.FromException<ResourceOpenHandleModel>(failure));
        grain.Setup(g => g.ReadAsync(It.IsAny<ResourceOpenHandleModel>(), 0, 10))
            .Returns(() => Task.FromException<byte[]>(failure));
        Func<Task> invoke = open
            ? () => adapter.OpenAsync(resource, 0, context, default).AsTask()
            : () => adapter.ReadAsync(handle, 0, 10, default).AsTask();
        Assert.Equal("denied", (await Assert.ThrowsAsync<ResourceDirectoryRejectedException>(invoke)).Message);
        failure = new IOException("lost reply");
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(invoke));
    }

    [Fact]
    public async Task OpenHandleStatBypassesNamespaceLookupAndPreservesCancellation()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var data = new Mock<IResourceDataOperations>(MockBehavior.Strict);
        var handle = new ResourceOpenHandle(GatewayTestContext.File, "retained", 2, 0);
        var expected = new ResourceStat(handle.Resource, "file", 0, 0, 0, 42, "u", "g", "u");
        using var cancellation = new CancellationTokenSource();
        data.Setup(x => x.StatAsync(handle.Resource, cancellation.Token)).ReturnsAsync(expected);
        var plane = new DistributedNamespaceDataPlane("group", new DistributedNamespaceOperations(factory.Object, data.Object));
        Assert.Equal(expected, await plane.StatAsync(handle, cancellation.Token));
        data.VerifyAll();
        factory.VerifyNoOtherCalls();
    }

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
    [InlineData("wstat")]
    [InlineData("fwstat")]
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
            "wstat" => () => adapter.WStatAsync(root, ResourceWStat.Unchanged(), context, token).AsTask(),
            "fwstat" => () => adapter.WStatOpenAsync(handle, ResourceWStat.Unchanged(), context, token).AsTask(),
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
    public async Task CreateUsesTheProcessGroupSnapshotAndBindValidatesBeforeResolvingIt()
    {
        var test = new GatewayTestContext();
        var operations = new DistributedNamespaceOperations(test.Factory.Object, test.Resources.Object);
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", GatewayTestContext.Root) });
        test.Resources.Setup(r => r.CreateAsync(GatewayTestContext.Root, "new", true, default)).ReturnsAsync(GatewayTestContext.File);
        Assert.Equal(GatewayTestContext.File, await operations.CreateAsync("group", channel, "new", true));
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var guarded = new DistributedNamespaceOperations(factory.Object, test.Resources.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => guarded.BindAsync(" ", channel, GatewayTestContext.Root));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.BindAsync("group", channel, GatewayTestContext.Root, cancellationToken: new CancellationToken(true)));
        factory.VerifyNoOtherCalls();
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
    public async Task DistributedNamespaceMutationsRejectInvalidArgumentsBeforeResolvingAGroup()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var operations = new DistributedNamespaceOperations(factory.Object, new Mock<IResourceOperations>().Object);
        ResourceHandle root = GatewayTestContext.Root;

        await Assert.ThrowsAsync<ArgumentException>(() => operations.MountAsync(" ", root, root));
        await Assert.ThrowsAsync<ArgumentNullException>(() => operations.MountAsync("group", null!, root));
        await Assert.ThrowsAsync<ArgumentNullException>(() => operations.MountAsync("group", root, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.UnmountAsync(" ", root));
        await Assert.ThrowsAsync<ArgumentNullException>(() => operations.UnmountAsync("group", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.SetMountsDisabledAsync(" ", true));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.SetMountDeviceBlockedAsync(" ", "device", true));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.SetMountDeviceBlockedAsync("group", " ", true));

        factory.VerifyNoOtherCalls();
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.SetMountsDisabledAsync("group", true, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.SetMountDeviceBlockedAsync("group", "device", true, cancelled));
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DistributedMountPolicyOperationsDelegateToTheProcessGroup()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var group = new Mock<IVProcessGroupGrain>(MockBehavior.Strict);
        factory.Setup(value => value.GetGrain<IVProcessGroupGrain>("group", null)).Returns(group.Object);
        group.Setup(value => value.SetMountsDisabledAsync(true)).Returns(Task.CompletedTask);
        group.Setup(value => value.SetMountDeviceBlockedAsync("device", true)).Returns(Task.CompletedTask);
        var operations = new DistributedNamespaceOperations(factory.Object, new Mock<IResourceOperations>().Object);

        await operations.SetMountsDisabledAsync("group", true);
        await operations.SetMountDeviceBlockedAsync("group", "device", true);

        group.VerifyAll();
    }

    [Fact]
    public async Task RforkNamespaceCanReplaceTheCurrentProcessGroup()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var grain = new Mock<IVProcessGrain>(MockBehavior.Strict);
        factory.Setup(value => value.GetGrain<IVProcessGrain>(7, null)).Returns(grain.Object);
        NamespaceChannelModel channel = new(new[]
        {
            new ChannelFrameModel("/", GatewayTestContext.Root.ToModel(), null, null),
        });
        var state = new VProcessStateModel(7, null, "rfork-group", channel, channel);
        var updated = state with { ProcessGroupId = "vprocess-7-rfork-copy" };
        grain.Setup(value => value.RforkNamespaceAsync(NamespaceForkModeModel.Copy, false)).ReturnsAsync(updated);

        VProcessStateModel result = await grain.Object.RforkNamespaceAsync(NamespaceForkModeModel.Copy);

        Assert.Equal("vprocess-7-rfork-copy", result.ProcessGroupId);
        grain.VerifyAll();
    }
}

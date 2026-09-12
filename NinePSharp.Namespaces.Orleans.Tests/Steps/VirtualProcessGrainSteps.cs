using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Steps;

[Binding]
[Scope(Feature = "Orleans virtual process namespace ownership")]
public sealed class VirtualProcessGrainSteps
{
    private static long nextProcessId;
    private readonly ResourceHandleModel initialMountPoint = Directory("initial-point", 10);
    private readonly ResourceHandleModel initialTarget = Directory("initial-target", 11);
    private readonly ResourceHandleModel laterMountPoint = Directory("later-point", 20);
    private readonly ResourceHandleModel laterTarget = Directory("later-target", 21);
    private IVProcessGrain? parent;
    private VProcessStateModel? parentState;
    private VProcessStateModel? childState;
    private NamespaceChannel? remoteChannel;
    private NamespaceWalkResult? remoteWalk;
    private ResourceHandleModel? remoteRoot;
    private ResourceHandleModel? localMountPoint;

    [Given("an initialized Orleans virtual process")]
    public async Task GivenInitializedProcess()
    {
        long processId = Interlocked.Increment(ref nextProcessId);
        string processGroupId = $"bdd-{Guid.NewGuid():N}";
        ResourceHandleModel root = Directory($"root-{processId}", 1);
        var rootChannel = new NamespaceChannelModel(
            new[] { new ChannelFrameModel("/", root, null, null) });
        parentState = new VProcessStateModel(processId, null, processGroupId, rootChannel, rootChannel);

        IVProcessGroupGrain group = OrleansTestEnvironment.Cluster.GrainFactory
            .GetGrain<IVProcessGroupGrain>(processGroupId);
        await group.InitializeEmptyAsync();
        parent = OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<IVProcessGrain>(processId);
        await parent.InitializeAsync(parentState);
    }

    [Given("the parent has an initial mount")]
    public async Task GivenInitialMount()
    {
        IVProcessGroupGrain group = ParentGroup();
        await group.MountAsync(initialTarget, initialMountPoint, MountFlags.Replace);
    }

    [Given("a remote resource grain is mounted on a local directory")]
    public async Task GivenRemoteResourceMounted()
    {
        Assert.NotNull(parentState);
        string device = $"resource-{Guid.NewGuid():N}";
        remoteRoot = Directory(device, 1);
        localMountPoint = Directory($"local-{device}", 100);
        await ParentGroup().MountAsync(remoteRoot, localMountPoint, MountFlags.Replace);
    }

    [When("it forks a child sharing its process group")]
    public Task ForkSharing() => ForkAsync(NamespaceForkModeModel.Share);

    [When("it forks a child copying its process group")]
    public Task ForkCopying() => ForkAsync(NamespaceForkModeModel.Copy);

    [When("it forks a child with an empty process group")]
    public Task ForkEmpty() => ForkAsync(NamespaceForkModeModel.Empty);

    [When("the parent mounts a resource")]
    [When("the parent mounts a different resource")]
    public async Task MountLaterResource()
    {
        IVProcessGroupGrain group = ParentGroup();
        await group.MountAsync(laterTarget, laterMountPoint, MountFlags.Replace);
    }

    [When("a child is walked through the distributed namespace")]
    public async Task WalkRemoteChild()
    {
        Assert.NotNull(parentState);
        Assert.NotNull(localMountPoint);
        var resources = new OrleansResourceOperations(
            new TestMountableResourceResolver(OrleansTestEnvironment.Cluster.GrainFactory));
        var operations = new DistributedNamespaceOperations(
            OrleansTestEnvironment.Cluster.GrainFactory,
            resources);
        remoteChannel = await operations.AttachAsync(parentState.ProcessGroupId, localMountPoint.ToDomain());
        remoteWalk = await operations.WalkAsync(parentState.ProcessGroupId, remoteChannel, new[] { "job" });
    }

    [When("the process rforks a copied namespace with mounts disabled")]
    public async Task RforkCopiedNamespace()
    {
        Assert.NotNull(parent);
        childState = await parent.RforkNamespaceAsync(NamespaceForkModeModel.Copy, noMounts: true);
    }

    [Then("the process has an independent namespace group")]
    public void ProcessHasIndependentNamespaceGroup()
    {
        Assert.NotNull(parentState);
        Assert.NotNull(childState);
        Assert.NotEqual(parentState.ProcessGroupId, childState.ProcessGroupId);
    }

    [Then("the copied namespace has mounts disabled")]
    public async Task CopiedNamespaceHasMountsDisabled()
    {
        Assert.NotNull(childState);
        NamespaceSnapshotModel snapshot = await OrleansTestEnvironment.Cluster.GrainFactory
            .GetGrain<IVProcessGroupGrain>(childState.ProcessGroupId)
            .GetSnapshotAsync();
        Assert.True(snapshot.MountsDisabled);
    }

    [Then("the child observes the parent mount")]
    public async Task ChildObservesParentMount()
    {
        MountHeadModel? mount = await ChildGroup().FindMountAsync(laterMountPoint.Identity);
        Assert.NotNull(mount);
        Assert.Equal(laterTarget, Assert.Single(mount.Mounts).Target);
    }

    [Then("the copied child retains the initial mount")]
    public async Task CopiedChildRetainsInitialMount()
    {
        MountHeadModel? mount = await ChildGroup().FindMountAsync(initialMountPoint.Identity);
        Assert.NotNull(mount);
        Assert.Equal(initialTarget, Assert.Single(mount.Mounts).Target);
    }

    [Then("the copied child does not observe the later mount")]
    public async Task CopiedChildDoesNotObserveLaterMount()
        => Assert.Null(await ChildGroup().FindMountAsync(laterMountPoint.Identity));

    [Then("the child mount table is empty")]
    public async Task ChildMountTableIsEmpty()
        => Assert.Empty((await ChildGroup().GetSnapshotAsync()).MountHeads);

    [Then("the child retains the parent root and current directory")]
    public void ChildRetainsChannels()
    {
        Assert.NotNull(parentState);
        Assert.NotNull(childState);
        Assert.True(parentState.Root.EquivalentTo(childState.Root));
        Assert.True(parentState.CurrentDirectory.EquivalentTo(childState.CurrentDirectory));
    }

    [Then("the test cluster contains two silos")]
    public void ClusterContainsTwoSilos()
        => Assert.Equal(2, OrleansTestEnvironment.Cluster.Silos.Count);

    [Then("the walk reaches the remote resource grain")]
    public void WalkReachesRemoteResource()
    {
        Assert.NotNull(remoteWalk);
        Assert.NotNull(remoteRoot);
        Assert.True(remoteWalk.Complete(1));
        Assert.Equal(remoteRoot.Identity.Device, remoteWalk.Channel.Current.Identity.Device);
        Assert.Equal(2UL, remoteWalk.Channel.Current.Identity.Path);
    }

    [Then("reading the remote directory returns its child")]
    public async Task ReadingRemoteDirectoryReturnsChild()
    {
        Assert.NotNull(parentState);
        Assert.NotNull(remoteChannel);
        var resources = new OrleansResourceOperations(
            new TestMountableResourceResolver(OrleansTestEnvironment.Cluster.GrainFactory));
        var operations = new DistributedNamespaceOperations(
            OrleansTestEnvironment.Cluster.GrainFactory,
            resources);
        IReadOnlyList<ResourceDirectoryEntry> entries = await operations.ReadDirectoryAsync(
            parentState.ProcessGroupId,
            remoteChannel);
        Assert.Equal("job", Assert.Single(entries).Name);
    }

    private static ResourceHandleModel Directory(string device, ulong path)
        => new(new ResourceIdentityModel("bdd-resource", device, path), QidType.QTDIR, 0);

    private async Task ForkAsync(NamespaceForkModeModel mode)
    {
        Assert.NotNull(parent);
        long childId = Interlocked.Increment(ref nextProcessId);
        childState = await parent.ForkAsync(childId, mode);
    }

    private IVProcessGroupGrain ParentGroup()
    {
        Assert.NotNull(parentState);
        return OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<IVProcessGroupGrain>(parentState.ProcessGroupId);
    }

    private IVProcessGroupGrain ChildGroup()
    {
        Assert.NotNull(childState);
        return OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<IVProcessGroupGrain>(childState.ProcessGroupId);
    }
}

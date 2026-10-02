using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Steps;

[Binding]
[Scope(Feature = "Failure-correct distributed resource operations")]
public sealed class DistributedResourceOperationSteps
{
    private string? device;
    private ResourceHandleModel? root;
    private ResourceOpenHandleModel? open;
    private ResourceOpenHandleModel? firstCreate;
    private ResourceOpenHandleModel? replayedCreate;
    private uint firstWrite;
    private uint replayedWrite;
    private NamespaceSession? session;
    private Exception? error;

    [Given("a distributed resource root")]
    public void GivenResourceRoot()
    {
        device = $"data-{Guid.NewGuid():N}";
        root = Handle(device, 1, true);
    }

    [Given("an open distributed resource file")]
    public async Task GivenOpenResourceFile()
    {
        GivenResourceRoot();
        open = await Resource().CreateAndOpenAsync(
            RequiredRoot(),
            "once",
            NinePConstants.Mode0644,
            NinePConstants.ORDWR,
            Context(10));
    }

    [Given("a mounted distributed resource session")]
    public async Task GivenMountedResourceSession()
    {
        GivenResourceRoot();
        string groupId = $"data-group-{Guid.NewGuid():N}";
        IVProcessGroupGrain group = OrleansTestEnvironment.Cluster.GrainFactory
            .GetGrain<IVProcessGroupGrain>(groupId);
        await group.InitializeEmptyAsync();
        var resources = new OrleansResourceOperations(
            new TestMountableResourceResolver(OrleansTestEnvironment.Cluster.GrainFactory));
        var operations = new DistributedNamespaceOperations(
            OrleansTestEnvironment.Cluster.GrainFactory,
            resources);
        session = new NamespaceSession(
            $"session-{Guid.NewGuid():N}",
            1,
            "glenda",
            new DistributedNamespaceDataPlane(groupId, operations));
        await session.AttachAsync(1, RequiredRoot().ToDomain());
    }

    [When("the session walks to job and opens it read-write")]
    public async Task WalkAndOpenJob()
    {
        await RequiredSession().WalkAsync(1, 2, new[] { "job" });
        await RequiredSession().OpenAsync(2, NinePConstants.ORDWR);
    }

    [When("the session writes distributed payload")]
    public async Task SessionWritesPayload()
        => await RequiredSession().WriteAsync(2, 0, Encoding.UTF8.GetBytes("distributed payload"));

    [When("create operation 10 creates once")]
    public async Task CreateOnce()
        => firstCreate = await Resource().CreateAndOpenAsync(
            RequiredRoot(),
            "once",
            NinePConstants.Mode0644,
            NinePConstants.ORDWR,
            Context(10));

    [When("the resource grain is migrated to another silo")]
    public async Task MigrateResource()
    {
        ITestMountableResourceGrain control = Control();
        TestResourceDiagnostics diagnostics = await control.GetDiagnosticsAsync();
        int before = diagnostics.Activations;
        var target = OrleansTestEnvironment.Cluster.GetActiveSilos()
            .Single(silo => !diagnostics.RuntimeIdentity.Contains(
                silo.SiloAddress.Endpoint.ToString(),
                StringComparison.Ordinal));
        await OrleansTestEnvironment.Cluster.MigrateAsync(Resource(), target.SiloAddress)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True((await Control().GetDiagnosticsAsync()).Activations > before);
    }

    [When("create operation 10 is replayed")]
    public async Task ReplayCreate()
        => replayedCreate = await Resource().CreateAndOpenAsync(
            RequiredRoot(),
            "once",
            NinePConstants.Mode0644,
            NinePConstants.ORDWR,
            Context(10));

    [When("write operation 20 stores payload")]
    public async Task WritePayload()
        => firstWrite = await Resource().WriteAsync(
            RequiredOpen(),
            0,
            Encoding.UTF8.GetBytes("payload"),
            Context(20));

    [When("write operation 20 is replayed")]
    public async Task ReplayWrite()
        => replayedWrite = await Resource().WriteAsync(
            RequiredOpen(),
            0,
            Encoding.UTF8.GetBytes("payload"),
            Context(20));

    [When("write operation 20 is reused with different data")]
    public async Task ReuseWriteWithDifferentData()
        => error = await Record.ExceptionAsync(
            () => Resource().WriteAsync(
                RequiredOpen(),
                0,
                Encoding.UTF8.GetBytes("different"),
                Context(20)));

    [Then("the session reads distributed payload")]
    public async Task SessionReadsPayload()
    {
        ReadOnlyMemory<byte> data = await RequiredSession().ReadAsync(2, 0, 100);
        Assert.Equal("distributed payload", Encoding.UTF8.GetString(data.Span));
    }

    [Then("the mounted stat name remains job")]
    public async Task MountedStatNameRemainsJob()
        => Assert.Equal("job", (await RequiredSession().StatAsync(2)).Name);

    [Then("the two create responses identify the same open handle")]
    public void CreateResponsesMatch()
        => Assert.Equal(RequiredFirstCreate(), Assert.IsType<ResourceOpenHandleModel>(replayedCreate));

    [Then("the resource contains one once child")]
    public async Task ResourceContainsOneChild()
        => Assert.Equal(1, (await Control().GetDiagnosticsAsync()).Children.Count(name => name == "once"));

    [Then("the resource records one mutation")]
    public async Task OneMutation()
        => Assert.Equal(1, (await Control().GetDiagnosticsAsync()).Mutations);

    [Then("both writes report the same count")]
    public void WritesReportSameCount() => Assert.Equal(firstWrite, replayedWrite);

    [Then("the resource records two mutations")]
    public async Task TwoMutations()
        => Assert.Equal(2, (await Control().GetDiagnosticsAsync()).Mutations);

    [Then("the operation identity collision is rejected")]
    public void CollisionRejected()
        => Assert.Contains("different request", Assert.IsType<InvalidOperationException>(error).Message);

    private static ResourceOperationContextModel Context(ulong sequence)
        => new(new ResourceOperationIdModel("retry-session", sequence), 1, "glenda");

    private static ResourceHandleModel Handle(string resourceDevice, ulong path, bool directory)
        => new(
            new ResourceIdentityModel("bdd-resource", resourceDevice, path),
            directory ? QidType.QTDIR : QidType.QTFILE,
            0);

    private IMountableResourceGrain Resource()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<IMountableResourceGrain>(RequiredDevice());

    private ITestMountableResourceGrain Control()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(RequiredDevice());

    private string RequiredDevice() => device ?? throw new InvalidOperationException("Device is missing.");

    private ResourceHandleModel RequiredRoot() => root ?? throw new InvalidOperationException("Root is missing.");

    private ResourceOpenHandleModel RequiredOpen() => open ?? throw new InvalidOperationException("Open handle is missing.");

    private ResourceOpenHandleModel RequiredFirstCreate()
        => firstCreate ?? throw new InvalidOperationException("Create result is missing.");

    private NamespaceSession RequiredSession() => session ?? throw new InvalidOperationException("Session is missing.");
}

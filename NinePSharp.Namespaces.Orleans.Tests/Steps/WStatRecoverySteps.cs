using NinePSharp.Constants;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Steps;

[Binding]
[Scope(Feature = "Durable wstat recovery resolves ambiguous replies safely")]
public sealed class WStatRecoverySteps
{
    private readonly string sessionId = $"wstat-recovery-{Guid.NewGuid():N}";
    private readonly string originalDevice = $"wstat-original-{Guid.NewGuid():N}";
    private readonly string replacementDevice = $"wstat-replacement-{Guid.NewGuid():N}";
    private readonly VProcessTable processes = new();
    private ResourceHandle? original;
    private ResourceHandle? replacement;
    private ResourceOpenHandle? open;
    private VProcess? process;
    private DurableFileStatOperations? gateway;
    private Plan9FileSyscalls? pathSyscalls;
    private IWStatRecoveryStore? journal;
    private Exception? pendingError;
    private Exception? collisionError;
    private uint? recoveredResult;
    private uint expectedResult;
    private ulong currentOperation;
    private bool pathOperation;

    [Given("a durable wstat operation targeting the distributed job file")]
    public async Task GivenDurableOpenWStat()
    {
        await InitializeAsync();
        open = await Resources().OpenAsync(
            RequiredOriginal(),
            NinePConstants.ORDWR,
            Context(900),
            CancellationToken.None);
    }

    [Given("a durable path wstat operation targeting the distributed job file")]
    public async Task GivenDurablePathWStat()
    {
        pathOperation = true;
        await InitializeAsync();
    }

    [Given("the provider will lose the next successful wstat reply")]
    public async Task GivenLostReply()
        => await OriginalControl().LoseNextWStatReplyAsync();

    [When("the gateway submits a mode change with operation (.*)")]
    public async Task SubmitModeChange(ulong sequence)
    {
        currentOperation = sequence;
        byte[] update = Update(NinePConstants.Mode0600);
        expectedResult = checked((uint)update.Length);
        pendingError = await Record.ExceptionAsync(async () =>
        {
            if (pathOperation)
            {
                await RequiredPathSyscalls().WStatAsync("/job", update);
            }
            else
            {
                await RequiredGateway().WStatAsync(RequiredOpen(), update, Context(sequence), CancellationToken.None);
            }
        });
    }

    [When("operation (.*) is resubmitted with a different mode")]
    public async Task ResubmitDifferentMode(ulong sequence)
        => collisionError = await Record.ExceptionAsync(
            () => RequiredGateway().WStatAsync(
                RequiredOpen(),
                Update(NinePConstants.Mode0755),
                Context(sequence),
                CancellationToken.None).AsTask());

    [Then("operation (.*) reports recovery pending and its journal intent remains pending")]
    public async Task PendingIntentRemains(ulong sequence)
    {
        WStatRecoveryPendingException error = Assert.IsType<WStatRecoveryPendingException>(pendingError);
        Assert.Equal(sequence, error.Context.OperationId.Sequence);
        WStatRecoveryRecord record = Assert.IsType<WStatRecoveryRecord>(
            await RequiredJournal().GetAsync(error.Context.OperationId, CancellationToken.None));
        Assert.Equal(WStatRecoveryState.Pending, record.State);
    }

    [When("a new gateway instance recovers operation (.*)")]
    public async Task RecoverFromNewGateway(ulong sequence)
    {
        gateway = NewGateway();
        recoveredResult = await gateway.RecoverAsync(Operation(sequence));
    }

    [Then("the original provider result is returned")]
    public void OriginalResultReturned()
        => Assert.Equal(expectedResult, Assert.IsType<uint>(recoveredResult));

    [Then("operation (.*) is durably committed with one provider mutation")]
    public async Task OperationCommittedOnce(ulong sequence)
    {
        WStatRecoveryRecord record = Assert.IsType<WStatRecoveryRecord>(
            await RequiredJournal().GetAsync(Operation(sequence), CancellationToken.None));
        Assert.Equal(WStatRecoveryState.Committed, record.State);
        Assert.Equal(expectedResult, record.Result);
        Assert.Equal(1, (await OriginalControl().GetDiagnosticsAsync()).Mutations);
    }

    [Then("the operation identity collision is rejected without another provider mutation")]
    public async Task CollisionRejectedWithoutMutation()
    {
        Assert.IsType<WStatRecoveryPendingException>(pendingError);
        Assert.Contains("different wstat request", Assert.IsType<InvalidOperationException>(collisionError).Message);
        Assert.Equal(1, (await OriginalControl().GetDiagnosticsAsync()).Mutations);
    }

    [When("a replacement resource becomes visible at the original path")]
    public async Task MountReplacementAtOriginalPath()
    {
        replacement = Job(replacementDevice);
        ResourceStat before = await Resources().StatAsync(replacement, CancellationToken.None);
        Assert.Equal(NinePConstants.Mode0644, before.Mode);
        RequiredProcess().ProcessGroup.MountTable.Mount(Root(replacementDevice), Root(originalDevice));

        NamespaceWalkResult visible = await new LocalNamespaceDataPlane(
            RequiredProcess().ProcessGroup.MountTable,
            Resources()).WalkAsync(
                RequiredProcess().Root,
                new[] { "job" },
                CancellationToken.None);
        Assert.Equal(replacement.Identity, visible.Channel.Current.Identity);
    }

    [When("the resource grain migrates before recovery")]
    public async Task MigrateOriginalResource()
    {
        ITestMountableResourceGrain control = OriginalControl();
        TestResourceDiagnostics diagnostics = await control.GetDiagnosticsAsync();
        var target = OrleansTestEnvironment.Cluster.GetActiveSilos()
            .Single(silo => !diagnostics.RuntimeIdentity.Contains(
                silo.SiloAddress.Endpoint.ToString(),
                StringComparison.Ordinal));
        await OrleansTestEnvironment.Cluster.MigrateAsync(OriginalResource(), target.SiloAddress)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True((await OriginalControl().GetDiagnosticsAsync()).Activations > diagnostics.Activations);
    }

    [Then("recovery changes only the originally selected resource")]
    public async Task OriginalResourceChanged()
    {
        Assert.Equal(expectedResult, Assert.IsType<uint>(recoveredResult));
        Assert.Equal(NinePConstants.Mode0600,
            (await Resources().StatAsync(RequiredOriginal(), CancellationToken.None)).Mode);
        Assert.Equal(1, (await OriginalControl().GetDiagnosticsAsync()).Mutations);
    }

    [Then("the replacement resource remains unchanged")]
    public async Task ReplacementUnchanged()
    {
        Assert.Equal(NinePConstants.Mode0644,
            (await Resources().StatAsync(RequiredReplacement(), CancellationToken.None)).Mode);
        Assert.Equal(0, (await ReplacementControl().GetDiagnosticsAsync()).Mutations);
    }

    [AfterScenario]
    public async Task ReleaseProcessAsync()
    {
        if (process is not null)
            await processes.TerminateAsync(process.Id);
    }

    private async Task InitializeAsync()
    {
        original = Job(originalDevice);
        ResourceHandle root = Root(originalDevice);
        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) });
        process = processes.CreateInitial(channel);
        var plane = new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, Resources());
        journal = new OrleansWStatRecoveryStore(OrleansTestEnvironment.Cluster.GrainFactory);
        gateway = NewGateway();
        pathSyscalls = new Plan9FileSyscalls(
            process,
            plane,
            () => Context(currentOperation),
            fileStats: gateway);
        Assert.Equal("job", (await Resources().StatAsync(original, CancellationToken.None)).Name);
    }

    private DurableFileStatOperations NewGateway()
        => new(
            new FileStatOperations(
                Resources(),
                new[]
                {
                    new DirectoryDeviceBinding(7, 0, "bdd-resource", originalDevice),
                    new DirectoryDeviceBinding(7, 1, "bdd-resource", replacementDevice),
                }),
            RequiredJournal());

    private OrleansResourceOperations Resources()
        => new(new WStatResourceResolver(OrleansTestEnvironment.Cluster.GrainFactory));

    private IMountableResourceGrain OriginalResource()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<IMountableResourceGrain>(originalDevice);

    private ITestMountableResourceGrain OriginalControl()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(originalDevice);

    private ITestMountableResourceGrain ReplacementControl()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(replacementDevice);

    private static ResourceHandle Root(string device)
        => new(new ResourceIdentity("bdd-resource", device, 1), QidType.QTDIR);

    private static ResourceHandle Job(string device)
        => new(new ResourceIdentity("bdd-resource", device, 2), QidType.QTFILE);

    private static byte[] Update(uint mode)
        => FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Mode = mode });

    private ResourceOperationId Operation(ulong sequence) => new(sessionId, sequence);

    private ResourceOperationContext Context(ulong sequence)
        => new(Operation(sequence), 1, "glenda");

    private ResourceHandle RequiredOriginal()
        => original ?? throw new InvalidOperationException("The original resource is missing.");

    private ResourceHandle RequiredReplacement()
        => replacement ?? throw new InvalidOperationException("The replacement resource is missing.");

    private ResourceOpenHandle RequiredOpen()
        => open ?? throw new InvalidOperationException("The open resource is missing.");

    private VProcess RequiredProcess()
        => process ?? throw new InvalidOperationException("The process is missing.");

    private DurableFileStatOperations RequiredGateway()
        => gateway ?? throw new InvalidOperationException("The gateway is missing.");

    private Plan9FileSyscalls RequiredPathSyscalls()
        => pathSyscalls ?? throw new InvalidOperationException("The path syscalls are missing.");

    private IWStatRecoveryStore RequiredJournal()
        => journal ?? throw new InvalidOperationException("The journal is missing.");

    private sealed class WStatResourceResolver : IMountableResourceResolver
    {
        private readonly IGrainFactory grains;

        internal WStatResourceResolver(IGrainFactory grains) => this.grains = grains;

        public IMountableResourceGrain Resolve(ResourceIdentityModel identity)
            => grains.GetGrain<IWStatResourceGrain>(identity.Device);
    }
}

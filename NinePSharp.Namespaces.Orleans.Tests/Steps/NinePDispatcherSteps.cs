using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using NinePSharp.Parser;
using NinePSharp.Server;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Steps;

[Binding]
[Scope(Feature = "A Plan 9 client uses the distributed namespace dispatcher")]
public sealed class NinePDispatcherSteps
{
    private const string User = "glenda";
    private readonly string sessionId = $"wire-{Guid.NewGuid():N}";
    private readonly string groupId = $"wire-group-{Guid.NewGuid():N}";
    private readonly string device = $"wire-resource-{Guid.NewGuid():N}";
    private NinePDialect dialect = NinePDialect.NineP2000;
    private DistributedNamespaceDispatcher? dispatcher;
    private object? readResponse;
    private object? flushResponse;
    private object? linuxCreateResponse;
    private object? linuxDirectoryOpenResponse;
    private object? unsupportedResponse;
    private object? versionResponse;
    private TimeSpan flushDuration;

    [Given("a distributed 9P dispatcher and attached fid 1")]
    public Task GivenDispatcherAndAttach()
        => CreateAndAttachAsync(NinePDialect.NineP2000);

    [Given("a distributed 9P2000.L dispatcher and attached fid 1")]
    public Task GivenLinuxDispatcherAndAttach()
        => CreateAndAttachAsync(NinePDialect.NineP2000L);

    [Given("9P walks job to fid 2 and opens it read-write")]
    [When("9P walks job to fid 2 and opens it read-write")]
    public async Task WalkAndOpen()
    {
        object walk = await DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "job" })));
        Assert.Single(Assert.IsType<Rwalk>(walk).Wqid);
        object open = await DispatchAsync(NinePMessage.NewMsgTopen(new Topen(3, 2, NinePConstants.ORDWR)));
        Assert.IsType<Ropen>(open);
    }

    [When("9P opens fid 1 as a directory")]
    public async Task OpenClassicDirectory()
    {
        object response = await DispatchAsync(
            NinePMessage.NewMsgTopen(new Topen(24, 1, NinePConstants.OREAD)));
        Assert.IsType<Ropen>(response);
    }

    [Given("the next resource read is delayed")]
    public async Task DelayRead()
        => await Control().DelayNextReadAsync(1000);

    [When("9P writes wire payload to fid 2")]
    public async Task WritePayload()
    {
        object response = await DispatchAsync(
            NinePMessage.NewMsgTwrite(new Twrite(4, 2, 0, Encoding.UTF8.GetBytes("wire payload"))));
        Assert.Equal(12U, Assert.IsType<Rwrite>(response).Count);
    }

    [When("the 9P transport session closes")]
    public async Task CloseTransport() => await RequiredDispatcher().CloseSessionAsync(sessionId);

    [When("9P reads fid 2 and flushes its request")]
    public async Task ReadAndFlush()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Task<object> read = DispatchAsync(NinePMessage.NewMsgTread(new Tread(10, 2, 0, 100)));
        await Task.Delay(50);
        flushResponse = await DispatchAsync(NinePMessage.NewMsgTflush(new Tflush(11, 10)));
        flushDuration = stopwatch.Elapsed;
        readResponse = await read;
    }

    [When("9P2000.L walks job to fid 2 and opens it read-write")]
    public async Task LinuxWalkAndOpen()
    {
        object walk = await DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(13, 1, 2, new[] { "job" })));
        Assert.Single(Assert.IsType<Rwalk>(walk).Wqid);
        object open = await DispatchAsync(NinePMessage.NewMsgTlopen(new Tlopen(15, 14, 2, 2)));
        Assert.IsType<Rlopen>(open);
    }

    [When("9P2000.L opens fid 1 as a directory")]
    public async Task LinuxOpenDirectory()
    {
        object response = await DispatchAsync(
            NinePMessage.NewMsgTlopen(new Tlopen(15, 15, 1, 0x10000)));
        Assert.IsType<Rlopen>(response);
    }

    [When("9P2000.L walks job to fid 2 and requests a directory-only open")]
    public async Task LinuxDirectoryOnlyOpenOnFile()
    {
        object walk = await DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(21, 1, 2, new[] { "job" })));
        Assert.Single(Assert.IsType<Rwalk>(walk).Wqid);
        linuxDirectoryOpenResponse = await DispatchAsync(
            NinePMessage.NewMsgTlopen(new Tlopen(15, 22, 2, 0x10000)));
    }

    [When("9P2000.L creates result on fid 1")]
    public async Task LinuxCreate()
    {
        linuxCreateResponse = await DispatchAsync(
            NinePMessage.NewMsgTlcreate(new Tlcreate(31, 16, 1, "result", 0x42, 0x1A4, uint.MaxValue)));
    }

    [When("9P2000.L sends unsupported (.*) with tag 20")]
    public async Task LinuxUnsupportedOperation(string operation)
    {
        NinePMessage request = operation switch
        {
            "symlink" => NinePMessage.NewMsgTsymlink(new Tsymlink(26, 20, 1, "link", "job", 0)),
            "rename" => NinePMessage.NewMsgTrename(new Trename(21, 20, 1, 1, "name")),
            "readlink" => NinePMessage.NewMsgTreadlink(new Treadlink(11, 20, 1)),
            "xattrwalk" => NinePMessage.NewMsgTxattrwalk(new Txattrwalk(21, 20, 1, 2, "name")),
            "fsync" => NinePMessage.NewMsgTfsync(new Tfsync(15, 20, 1, 0)),
            "link" => NinePMessage.NewMsgTlink(new Tlink(21, 20, 1, 1, "name")),
            "unlinkat" => NinePMessage.NewMsgTunlinkat(new Tunlinkat(20, 20, 1, "job", 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        unsupportedResponse = await DispatchAsync(request);
    }

    [When("the client negotiates 9P2000.L")]
    public async Task NegotiateLinuxVersion()
        => versionResponse = await DispatchAsync(
            NinePMessage.NewMsgTversion(
                new Tversion(NinePConstants.NoTag, NinePConstants.DefaultMSize, NinePConstants.VersionString9pl)));

    [Then("9P reads wire payload from fid 2")]
    public async Task ReadPayload()
    {
        object response = await DispatchAsync(NinePMessage.NewMsgTread(new Tread(5, 2, 0, 100)));
        Assert.Equal("wire payload", Encoding.UTF8.GetString(Assert.IsType<Rread>(response).Data.Span));
    }

    [Then("9P stat reports job for fid 2")]
    public async Task StatReportsJob()
    {
        object response = await DispatchAsync(NinePMessage.NewMsgTstat(new Tstat(6, 2)));
        Assert.Equal("job", Assert.IsType<Rstat>(response).Stat.Name);
    }

    [Then("classic directory reads use complete stat records and valid offsets")]
    public async Task ClassicDirectoryReadsPreserveRecords()
    {
        var undersized = Assert.IsType<Rread>(
            await DispatchAsync(NinePMessage.NewMsgTread(new Tread(25, 1, 0, 1))));
        Assert.Empty(undersized.Data.ToArray());

        var firstPage = Assert.IsType<Rread>(
            await DispatchAsync(NinePMessage.NewMsgTread(new Tread(26, 1, 0, 1024))));
        Assert.NotEmpty(firstPage.Data.ToArray());

        var complete = Assert.IsType<Rread>(
            await DispatchAsync(
                NinePMessage.NewMsgTread(new Tread(27, 1, (ulong)firstPage.Data.Length, 1024))));
        Assert.Empty(complete.Data.ToArray());

        var malformed = Assert.IsType<Rerror>(
            await DispatchAsync(NinePMessage.NewMsgTread(new Tread(28, 1, 1, 1024))));
        Assert.Contains("directory boundary", malformed.Ename, StringComparison.Ordinal);

        var pastEnd = Assert.IsType<Rread>(
            await DispatchAsync(
                NinePMessage.NewMsgTread(new Tread(29, 1, (ulong)firstPage.Data.Length + 1, 1024))));
        Assert.Empty(pastEnd.Data.ToArray());
    }

    [Then("9P clunk invalidates fid 2")]
    public async Task ClunkInvalidatesFid()
    {
        Assert.IsType<Rclunk>(await DispatchAsync(NinePMessage.NewMsgTclunk(new Tclunk(7, 2))));
        var error = Assert.IsType<Rerror>(
            await DispatchAsync(NinePMessage.NewMsgTstat(new Tstat(8, 2))));
        Assert.Contains("unknown fid", error.Ename, StringComparison.Ordinal);
    }

    [Then("the resource grain records one clunk")]
    public async Task ResourceRecordsClunk()
        => Assert.Equal(1, (await Control().GetDiagnosticsAsync()).Clunks);

    [Then("later requests on fid 2 report a bad fid")]
    public async Task LaterRequestReportsBadFid()
    {
        var error = Assert.IsType<Rerror>(
            await DispatchAsync(NinePMessage.NewMsgTstat(new Tstat(12, 2))));
        Assert.Contains("unknown fid", error.Ename, StringComparison.Ordinal);
    }

    [Then("the read reports interruption before the resource delay ends")]
    public void ReadInterrupted()
    {
        Assert.Equal("interrupted", Assert.IsType<Rerror>(readResponse).Ename);
        Assert.True(flushDuration < TimeSpan.FromMilliseconds(800), $"Flush took {flushDuration}.");
    }

    [Then("the flush succeeds")]
    public void FlushSucceeds() => Assert.IsType<Rflush>(flushResponse);

    [Then("9P2000.L getattr reports a regular file for fid (.*)")]
    public async Task LinuxGetAttrReportsRegularFile(uint fid)
    {
        const ulong modeAndSize = 0x201;
        object response = await DispatchAsync(
            NinePMessage.NewMsgTgetattr(new Tgetattr(17, fid, modeAndSize)));
        var getattr = Assert.IsType<Rgetattr>(response);
        Assert.Equal(modeAndSize, getattr.Valid);
        Assert.Equal(0x8000U, getattr.Mode & 0xF000U);
    }

    [Then("9P2000.L readdir reports job as a regular file")]
    public async Task LinuxReadDirectoryReportsJob()
    {
        object response = await DispatchAsync(
            NinePMessage.NewMsgTreaddir(new Treaddir(23, 18, 1, 0, 1024)));
        ReadOnlySpan<byte> data = Assert.IsType<Rreaddir>(response).Data.Span;
        Assert.True(data.Length >= 25);
        ulong cookie = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(13, 8));
        Assert.Equal((ulong)data.Length, cookie);
        Assert.Equal(8, data[21]);
        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(22, 2));
        Assert.Equal("job", Encoding.UTF8.GetString(data.Slice(24, nameLength)));
    }

    [Then("9P2000.L returns an open result fid")]
    public void LinuxCreateReturnsOpenFid() => Assert.IsType<Rlcreate>(linuxCreateResponse);

    [Then("9P2000.L returns tag 20 with operation not supported")]
    public void LinuxUnsupportedResponse()
    {
        var response = Assert.IsType<Rlerror>(unsupportedResponse);
        Assert.Equal(20, response.Tag);
        Assert.Equal((uint)LinuxErrorCode.EOPNOTSUPP, response.Ecode);
    }

    [Then("9P2000.L reports not a directory and fid 2 can still be opened")]
    public async Task LinuxDirectoryOnlyFailurePreservesFid()
    {
        var error = Assert.IsType<Rlerror>(linuxDirectoryOpenResponse);
        Assert.Equal((uint)LinuxErrorCode.ENOTDIR, error.Ecode);
        object opened = await DispatchAsync(NinePMessage.NewMsgTlopen(new Tlopen(15, 23, 2, 0)));
        Assert.IsType<Rlopen>(opened);
    }

    [Then("the negotiated version is 9P2000.L")]
    public void NegotiatedVersionIsLinux()
        => Assert.Equal(
            NinePConstants.VersionString9pl,
            Assert.IsType<Rversion>(versionResponse).Version);

    private async Task CreateAndAttachAsync(NinePDialect requestedDialect)
    {
        dialect = requestedDialect;
        IVProcessGroupGrain group = OrleansTestEnvironment.Cluster.GrainFactory
            .GetGrain<IVProcessGroupGrain>(groupId);
        await group.InitializeEmptyAsync();
        var resources = new OrleansResourceOperations(
            new TestMountableResourceResolver(OrleansTestEnvironment.Cluster.GrainFactory));
        var operations = new DistributedNamespaceOperations(
            OrleansTestEnvironment.Cluster.GrainFactory,
            resources);
        dispatcher = new DistributedNamespaceDispatcher(
            operations,
            new FixedAttachResolver(groupId, device));

        object response = await DispatchAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 1, NinePConstants.NoFid, User, string.Empty)));
        Assert.IsType<Rattach>(response);
    }

    private Task<object> DispatchAsync(NinePMessage message)
        => RequiredDispatcher().DispatchAsync(sessionId, message, dialect);

    private DistributedNamespaceDispatcher RequiredDispatcher()
        => dispatcher ?? throw new InvalidOperationException("Dispatcher is missing.");

    private ITestMountableResourceGrain Control()
        => OrleansTestEnvironment.Cluster.GrainFactory.GetGrain<ITestMountableResourceGrain>(device);

    private sealed class FixedAttachResolver : IDistributedNamespaceAttachResolver
    {
        private readonly string processGroupId;
        private readonly ResourceHandle root;

        internal FixedAttachResolver(string processGroupId, string device)
        {
            this.processGroupId = processGroupId;
            root = new ResourceHandle(
                new ResourceIdentity("bdd-resource", device, 1),
                QidType.QTDIR);
        }

        public ValueTask<DistributedNamespaceAttach> ResolveAsync(
            string sessionId,
            Tattach request,
            NinePDialect dialect,
            System.Security.Cryptography.X509Certificates.X509Certificate2? certificate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DistributedNamespaceAttach(
                processGroupId,
                1,
                request.Uname,
                root));
        }
    }
}

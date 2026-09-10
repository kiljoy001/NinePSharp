using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Plan 9 compatible namespace fid sessions")]
public sealed class FidSessionSteps
{
    private MemoryDataResources? resources;
    private ResourceHandle? root;
    private NamespaceSession? first;
    private NamespaceSession? second;
    private NamespaceWalkResult? walk;
    private Exception? error;

    [Given("a namespace session attached with fid 1")]
    public async Task GivenAttachedSession()
    {
        resources = new MemoryDataResources();
        root = resources.Directory("root", "child");
        first = CreateSession("first");
        await first.AttachAsync(1, root);
    }

    [Given("two namespace sessions attached with fid 1")]
    public async Task GivenTwoAttachedSessions()
    {
        resources = new MemoryDataResources();
        root = resources.Directory("root", "child");
        first = CreateSession("first");
        second = CreateSession("second");
        await first.AttachAsync(1, root);
        await second.AttachAsync(1, root);
    }

    [Given("fid 1 is opened for reading")]
    public async Task GivenOpenForReading()
        => await RequiredFirst().OpenAsync(1, NinePConstants.OREAD);

    [Given("provider clunk will fail")]
    public void GivenClunkFailure() => RequiredResources().FailClunk = true;

    [When("the first session walks fid 1 to child as fid 2")]
    public async Task WalkFirstSession()
        => walk = await RequiredFirst().WalkAsync(1, 2, new[] { "child" });

    [When("fid 1 partially walks child then missing as fid 2")]
    public async Task PartialWalk()
        => walk = await RequiredFirst().WalkAsync(1, 2, new[] { "child", "missing" });

    [When("the client attempts to walk the open fid")]
    public async Task WalkOpenFid()
        => error = await Record.ExceptionAsync(
            () => RequiredFirst().WalkAsync(1, 2, new[] { "child" }).AsTask());

    [When("the client creates result with fid 1")]
    public async Task CreateResult()
        => await RequiredFirst().CreateAsync(1, "result", NinePConstants.Mode0644, NinePConstants.ORDWR);

    [When("writes payload through fid 1")]
    public async Task WritePayload()
        => await RequiredFirst().WriteAsync(1, 0, Encoding.UTF8.GetBytes("payload"));

    [When("the client clunks fid 1")]
    public async Task ClunkFid()
        => error = await Record.ExceptionAsync(() => RequiredFirst().ClunkAsync(1).AsTask());

    [When("the namespace session is closed")]
    public async Task CloseSession() => await RequiredFirst().DisposeAsync();

    [Then("only the first session contains fid 2")]
    public void OnlyFirstContainsFid()
    {
        Assert.True(RequiredFirst().ContainsFid(2));
        Assert.False(RequiredSecond().ContainsFid(2));
    }

    [Then("the walk returns one qid")]
    public void WalkReturnsOneQid() => Assert.Single(Assert.IsType<NamespaceWalkResult>(walk).Qids);

    [Then("fid 2 is not allocated")]
    public void NewFidNotAllocated() => Assert.False(RequiredFirst().ContainsFid(2));

    [Then("fid 1 still selects the root")]
    public async Task OriginalFidAtRoot()
    {
        ResourceStat stat = await RequiredFirst().StatAsync(1);
        Assert.Equal("/", stat.Name);
    }

    [Then("the walk is rejected as an open fid")]
    public void OpenWalkRejected()
        => Assert.Contains("open fid", Assert.IsType<NamespaceFidException>(error).Message, StringComparison.Ordinal);

    [Then("fid 1 reads payload from result")]
    public async Task FidReadsPayload()
    {
        ReadOnlyMemory<byte> data = await RequiredFirst().ReadAsync(1, 0, 100);
        Assert.Equal("payload", Encoding.UTF8.GetString(data.Span));
    }

    [Then("stat reports the visible name result")]
    public async Task StatReportsVisibleName()
        => Assert.Equal("result", (await RequiredFirst().StatAsync(1)).Name);

    [Then("the clunk failure is reported")]
    public void ClunkFailureReported() => Assert.IsType<IOException>(error);

    [Then("fid 1 is no longer allocated")]
    public void FidNotAllocated() => Assert.False(RequiredFirst().ContainsFid(1));

    [Then("its provider open handle is clunked")]
    public void ProviderHandleClunked() => Assert.Equal(1, RequiredResources().ClunkCount);

    private NamespaceSession CreateSession(string id)
        => new(id, 1, "glenda", new LocalNamespaceDataPlane(new MountTable(), RequiredResources()));

    private NamespaceSession RequiredFirst() => first ?? throw new InvalidOperationException("First session is missing.");

    private NamespaceSession RequiredSecond() => second ?? throw new InvalidOperationException("Second session is missing.");

    private MemoryDataResources RequiredResources()
        => resources ?? throw new InvalidOperationException("Resources are missing.");
}

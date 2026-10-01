using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Authorization.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Authorization.Tests.Steps;

[Binding]
[Scope(Feature = "Resource operations in a principal's view are authorized")]
public sealed class ResourceAuthorizationSteps
{
    private readonly TreeResources tree = new();
    private readonly MountTable mounts = new();
    private readonly List<AuthorizationPrincipal> principals = new();
    private readonly List<GroupMembership> memberships = new();
    private readonly List<ResourceGrant> grants = new();
    private readonly List<ResourceIdentity> readOnlyRoots = new();
    private readonly Dictionary<string, AuthorizedResourceOperations> views = new(StringComparer.Ordinal);
    private ulong policyGeneration = 1;
    private ulong currentGeneration = 1;
    private bool reportParents = true;
    private ulong sequence;
    private Exception? error;
    private object? result;
    private string requestKind = string.Empty;
    private string requestPath = string.Empty;
    private int callsBefore;
    private ResourceOpenHandle? retained;
    private string retainedUser = string.Empty;
    private string retainedPath = string.Empty;
    private bool createdDirectory;
    private string chainFile = string.Empty;
    private Func<AuthorizationPolicy>? pendingPolicy;

    [Given("the resource tree")]
    public void GivenTree(DataTable table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            bool directory = row["kind"] == "directory";
            uint mode = Octal(row["mode"]) | (directory ? (uint)NinePConstants.FileMode9P.DMDIR : 0);
            tree.Add(row["path"], directory, row["owner"], row["group"], mode);
        }
    }

    [Given("the principals")]
    public void GivenPrincipals(DataTable table)
    {
        foreach (DataTableRow row in table.Rows)
            principals.Add(new AuthorizationPrincipal(row["user"], bool.Parse(row["enabled"])));
        views.Clear();
    }

    [Given(@"^group ""(.*)"" has members ""(.*)""$")]
    public void GivenGroup(string group, string members)
    {
        foreach (string member in members.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            memberships.Add(new GroupMembership(group, member));
        views.Clear();
    }

    [Given(@"^the policy generation is (\d+)$")]
    public void GivenGeneration(ulong generation)
    {
        policyGeneration = generation;
        currentGeneration = generation;
        views.Clear();
    }

    [Given("the grants")]
    public void GivenGrants(DataTable table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            grants.Add(new ResourceGrant(
                Enum.Parse<GrantSubjectKind>(row["kind"], ignoreCase: true),
                row["subject"],
                tree.Handle(row["path"]).Identity,
                Enum.Parse<GrantScope>(row["scope"], ignoreCase: true),
                Rights(row["rights"])));
        }

        views.Clear();
    }

    [Given("nothing else changes")]
    public void NothingElseChanges()
    {
    }

    [Given(@"^the policy generation becomes (\d+)$")]
    [When(@"^the policy generation becomes (\d+)$")]
    public void GenerationBecomes(ulong generation) => currentGeneration = generation;

    [Given(@"^""(.*)"" has mode (\d+)$")]
    [When(@"^""(.*)"" has mode (\d+)$")]
    public void HasMode(string path, string mode) => tree.SetMode(path, Octal(mode));

    [Given(@"^""(.*)"" is owned by ""(.*)""$")]
    public void OwnedBy(string path, string owner) => tree.SetOwner(path, owner);

    [Given(@"^""(.*)"" is mounted read-only$")]
    public void MountedReadOnly(string path)
    {
        readOnlyRoots.Add(tree.Handle(path).Identity);
        views.Clear();
    }

    [Given(@"^a union at ""(.*)"" of ""(.*)"" before ""(.*)""$")]
    public void GivenUnion(string at, string first, string second)
    {
        Assert.Equal(at, second);
        mounts.Mount(tree.Handle(first), tree.Handle(at), MountFlags.Before);
    }

    [Given("the provider does not report parents")]
    public void ProviderWithoutParents()
    {
        reportParents = false;
        views.Clear();
    }

    [Given(@"^the provider reports ""(.*)"" and ""(.*)"" as each other's parent$")]
    public void ParentCycle(string first, string second)
    {
        tree.ReportParent(first, second);
        tree.ReportParent(second, first);
    }

    [Given(@"^(\w+) has opened ""(.*)"" for (\S+)$")]
    public async Task GivenOpened(string user, string path, string mode)
    {
        retained = await View(user).OpenAsync(tree.Handle(path), OpenMode(mode), Context(user), CancellationToken.None);
        retainedUser = user;
        retainedPath = path;
    }

    [When(@"^(\w+) opens ""(.*)"" for (\S+)$")]
    public Task Opens(string user, string path, string mode) => OpenAs(user, path, mode, user);

    [When(@"^(\w+) opens ""(.*)"" for (\S+) with an operation context naming (\w+)$")]
    public Task OpensClaiming(string user, string path, string mode, string claimed) => OpenAs(user, path, mode, claimed);

    [When(@"^(\w+) opens the handle of ""(.*)"" obtained elsewhere for (\S+)$")]
    public Task OpensForeign(string user, string path, string mode) => OpenAs(user, path, mode, user);

    [When(@"^(\w+) stats ""(.*)""$")]
    public Task Stats(string user, string path)
        => Attempt("stat", path, async () => await View(user).StatAsync(tree.Handle(path), CancellationToken.None));

    [When(@"^(\w+) stats the handle of ""(.*)"" obtained elsewhere$")]
    public Task StatsForeign(string user, string path) => Stats(user, path);

    [When(@"^(\w+) writes ""(.*)"" through that handle$")]
    public Task WritesThroughHandle(string user, string data)
        => Attempt("write", retainedPath, async () =>
            await View(user).WriteAsync(retained!, 0, Encoding.ASCII.GetBytes(data), Context(user), CancellationToken.None));

    [When(@"^(\w+) reads through a handle opened directly from the provider$")]
    public Task ReadsForeignHandle(string user)
    {
        ResourceOpenHandle direct = tree.OpenDirect("/data/report", NinePConstants.OREAD);
        return Attempt("read", "/data/report", async () => await View(user).ReadAsync(direct, 0, 64, CancellationToken.None));
    }

    [When(@"^(\w+) walks to ""(.*)""$")]
    public async Task Walks(string user, string path)
    {
        requestKind = "walk";
        requestPath = path;
        error = null;
        result = null;
        try
        {
            var plane = new LocalNamespaceDataPlane(mounts, View(user));
            NamespaceChannel root = await plane.AttachAsync(tree.Handle("/"), CancellationToken.None);
            string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            result = (await plane.WalkAsync(root, names, CancellationToken.None), names.Length);
        }
        catch (Exception caught)
        {
            error = caught;
        }
    }

    [When(@"^(\w+) lists ""(.*)""$")]
    [When(@"^(\w+) lists the union at ""(.*)""$")]
    public async Task Lists(string user, string path)
    {
        error = null;
        var plane = new LocalNamespaceDataPlane(mounts, View(user));
        NamespaceChannel root = await plane.AttachAsync(tree.Handle("/"), CancellationToken.None);
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        NamespaceWalkResult walk = await plane.WalkAsync(root, names, CancellationToken.None);
        Assert.True(walk.Complete(names.Length));
        result = await plane.ReadDirectoryAsync(walk.Channel, CancellationToken.None);
    }

    [When(@"^(\w+) creates (file|directory) ""(.*)"" in ""(.*)"" with permissions (\d+) for (\S+)$")]
    public Task Creates(string user, string kind, string name, string directory, string permissions, string mode)
    {
        createdDirectory = kind == "directory";
        uint perm = Octal(permissions) | (createdDirectory ? (uint)NinePConstants.FileMode9P.DMDIR : 0);
        return Attempt("create", directory, async () => await View(user).CreateAndOpenAsync(
            tree.Handle(directory), name, perm, OpenMode(mode), Context(user), CancellationToken.None));
    }

    [When(@"^(\w+) removes ""(.*)"" through that handle$")]
    public Task RemovesThroughHandle(string user, string path)
        => Attempt("remove", path, async () =>
        {
            await View(user).RemoveAsync(tree.Handle(path), retained, Context(user), CancellationToken.None);
            return true;
        });

    [When(@"^(\w+) removes ""(.*)""$")]
    public Task Removes(string user, string path)
        => Attempt("remove", path, async () =>
        {
            await View(user).RemoveAsync(tree.Handle(path), null, Context(user), CancellationToken.None);
            return true;
        });

    [When(@"^a view is built for (\w+)$")]
    public void ViewBuilt(string user)
    {
        error = null;
        try { result = View(user); }
        catch (Exception caught) { error = caught; }
    }

    [When(@"^(\w+) lists ""(.*)"" after revocation$")]
    [When(@"^(\w+) lists the directory ""(.*)"" directly$")]
    public Task ListsDirectly(string user, string path)
        => Attempt("readdir", path, async () => await View(user).ReadDirectoryAsync(tree.Handle(path), CancellationToken.None));

    [When(@"^(\w+) walks out of the file ""(.*)""$")]
    public Task WalksOutOfFile(string user, string path)
        => Attempt("walk", path, async () => (object?)await View(user).WalkAsync(tree.Handle(path), "child", CancellationToken.None) ?? "none");

    [When(@"^(\w+) reads through that handle$")]
    public Task ReadsThroughHandle(string user)
        => Attempt("read", retainedPath, async () => await View(user).ReadAsync(retained!, 0, 64, CancellationToken.None));

    [When(@"^(\w+) clunks a handle opened directly from the provider$")]
    public Task ClunksForeignHandle(string user)
    {
        ResourceOpenHandle direct = tree.OpenDirect("/data/report", NinePConstants.OREAD);
        return Attempt("clunk", "/data/report", async () =>
        {
            await View(user).ClunkAsync(direct, Context(user), CancellationToken.None);
            return true;
        });
    }

    [When(@"^(\w+) creates the entry ""(.*)"" in ""(.*)"" without opening it$")]
    public Task CreatesEntry(string user, string name, string directory)
        => Attempt("create", directory, async () => await View(user).CreateAsync(tree.Handle(directory), name, false, CancellationToken.None));

    [Given(@"^a chain of (\d+) nested directories under ""/"" ending in a file$")]
    public void GivenChain(int depth)
    {
        string path = string.Empty;
        for (int index = 1; index <= depth; index++)
        {
            path += $"/d{index}";
            tree.Add(path, true, "glenda", "sys", (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED);
        }

        chainFile = path + "/leaf";
        tree.Add(chainFile, false, "glenda", "sys", 0x1A4);
    }

    [Given(@"^(\w+) holds a read tree grant on ""(.*)""$")]
    public void GivenReadTree(string user, string path)
    {
        grants.Add(new ResourceGrant(GrantSubjectKind.User, user, tree.Handle(path).Identity, GrantScope.Tree,
            ResourceRights.Stat | ResourceRights.Read));
        views.Clear();
    }

    [When(@"^(\w+) opens the file at the end of the chain for (\S+)$")]
    public Task OpensChainFile(string user, string mode) => OpenAs(user, chainFile, mode, user);

    [Then("the write reaches the provider")]
    public void WriteReachesProvider()
    {
        Assert.Null(error);
        Assert.Equal(callsBefore + 1, tree.CallsMatching($"write {requestPath}"));
    }

    [Then("the entry is created")]
    public void EntryCreated()
    {
        Assert.Null(error);
        Assert.Equal(callsBefore + 1, tree.CallsMatching($"create {requestPath}"));
    }

    [Then("the request is a definite create rejection")]
    public void DefiniteCreateRejection()
    {
        Assert.Equal("permission denied", Assert.IsType<ResourceCreateRejectedException>(error).Message);
        Assert.Equal(callsBefore, tree.CallsMatching($"create {requestPath}"));
    }

    [When(@"^""(.*)"" moves to ""(.*)""$")]
    public void Moves(string path, string destination) => tree.Move(path, destination);

    [Then("the stat succeeds")]
    public void Succeeds()
    {
        Assert.Null(error);
        Assert.Equal(callsBefore + 1, tree.CallsMatching($"{requestKind} {requestPath}"));
    }

    [Then(@"^the open succeeds with the provider's identity for ""(.*)""$")]
    public void SucceedsWithIdentity(string path)
    {
        Succeeds();
        Assert.Equal(tree.Handle(path).Identity, Assert.IsType<ResourceOpenHandle>(result).Resource.Identity);
    }

    [Then("the request is denied before provider dispatch")]
    public void DeniedBeforeDispatch()
    {
        Assert.Equal("permission denied", Assert.IsType<ResourceAccessDeniedException>(error).Message);
        Assert.Equal(callsBefore, tree.CallsMatching($"{requestKind} {requestPath}"));
    }

    [Then("the request reports not found")]
    public void ReportsNotFound()
    {
        var notFound = Assert.IsType<NamespaceException>(error);
        Assert.Equal(NamespaceError.ResourceNotFound, notFound.Error);
        Assert.Equal("file does not exist", notFound.Message);
        Assert.Equal(callsBefore, tree.CallsMatching($"{requestKind} {requestPath}"));
    }

    [Then(@"^the open (succeeds|is denied before provider dispatch|reports not found)$")]
    public void OpenOutcome(string outcome)
    {
        if (outcome == "succeeds") Succeeds();
        else if (outcome == "reports not found") ReportsNotFound();
        else DeniedBeforeDispatch();
    }

    [Then(@"^the remove (reaches the provider|is denied before provider dispatch)$")]
    public void RemoveOutcome(string outcome)
    {
        if (outcome == "reaches the provider")
        {
            Assert.Null(error);
            Assert.Equal(callsBefore + 1, tree.CallsMatching($"remove {requestPath}"));
        }
        else
        {
            DeniedBeforeDispatch();
        }
    }

    [Then("the request is a definite directory rejection")]
    public void DirectoryRejection()
    {
        string message = Assert.IsType<ResourceDirectoryRejectedException>(error).Message;
        Assert.Contains(message, new[] { "permission denied", "file does not exist" });
        Assert.Equal(callsBefore, tree.CallsMatching($"{requestKind} {requestPath}"));
    }

    [Then("the request is a definite create rejection with no provider call")]
    public void CreateRejection()
    {
        Assert.Equal("permission denied", Assert.IsType<ResourceCreateRejectedException>(error).Message);
        Assert.Equal(0, tree.CallsMatching("create "));
    }

    [Then(@"^the provider receives permissions (\d+)$")]
    public void ProviderReceivesPermissions(string expected)
    {
        Assert.Null(error);
        uint received = Assert.NotNull(tree.LastCreatePermissions);
        Assert.Equal(Octal(expected), received & 0x1FF);
        Assert.Equal(createdDirectory, (received & (uint)NinePConstants.FileMode9P.DMDIR) != 0);
    }

    [Then(@"^the walk is denied at ""(.*)""$")]
    public void WalkDeniedAt(string directory)
    {
        Assert.IsType<ResourceAccessDeniedException>(error);
        NoWalkFrom(directory);
    }

    [Then(@"^no provider walk was made from ""(.*)""$")]
    public void NoWalkFrom(string directory) => Assert.Equal(0, tree.CallsMatching($"walk {directory} "));

    [Then("the walk succeeds")]
    public void WalkSucceeds()
    {
        Assert.Null(error);
        var (walk, expected) = ((NamespaceWalkResult, int))result!;
        Assert.True(walk.Complete(expected));
    }

    [Then("the walk reports not found")]
    public void WalkNotFound()
    {
        Assert.Null(error);
        var (walk, expected) = ((NamespaceWalkResult, int))result!;
        Assert.False(walk.Complete(expected));
    }

    [Then(@"^the listing contains exactly ""(.*)""$")]
    public void ListingContains(string names)
    {
        var listing = Assert.IsAssignableFrom<IReadOnlyList<ResourceStat>>(result);
        Assert.Equal(names.Split(',', StringSplitOptions.TrimEntries), listing.Select(stat => stat.Name).ToArray());
    }

    [Then(@"^(\w+) can read through that handle$")]
    public async Task CanRead(string user)
    {
        Assert.Equal(retainedUser, user);
        // Counted across paths: a moved file is logged under its new name.
        int before = tree.CallsMatching("read ");
        ReadOnlyMemory<byte> bytes = await View(user).ReadAsync(retained!, 0, 64, CancellationToken.None);
        Assert.False(bytes.IsEmpty);
        Assert.Equal(before + 1, tree.CallsMatching("read "));
    }

    [Then(@"^(reading|writing) through that handle is denied before provider dispatch$")]
    public async Task HandleDenied(string operation)
    {
        await (operation == "reading"
            ? Attempt("read", retainedPath, async () => await View(retainedUser).ReadAsync(retained!, 0, 64, CancellationToken.None))
            : Attempt("write", retainedPath, async () =>
                await View(retainedUser).WriteAsync(retained!, 0, new byte[] { 1 }, Context(retainedUser), CancellationToken.None)));
        DeniedBeforeDispatch();
    }

    [Then("clunking that handle reaches the provider")]
    public async Task ClunkReachesProvider()
    {
        int before = tree.CallsMatching("clunk ");
        await View(retainedUser).ClunkAsync(retained!, Context(retainedUser), CancellationToken.None);
        Assert.Equal(before + 1, tree.CallsMatching("clunk "));
    }

    [Then(@"^(\w+) is denied before provider dispatch for each of$")]
    public async Task DeniedForEach(string user, DataTable table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            string operation = row["operation"];
            string[] quoted = operation.Split('"');
            if (operation.StartsWith("open ", StringComparison.Ordinal))
            {
                await Opens(user, quoted[1], operation[(operation.LastIndexOf(' ') + 1)..]);
                DeniedBeforeDispatch();
            }
            else if (operation.StartsWith("create ", StringComparison.Ordinal))
            {
                await Creates(user, "file", quoted[1], quoted[3], "0664", "OWRITE");
                CreateRejection();
            }
            else
            {
                await Removes(user, quoted[1]);
                DeniedBeforeDispatch();
            }
        }
    }

    [Then("construction fails because tree grants need provider ancestry")]
    public void ConstructionNeedsAncestry() => Assert.IsType<ArgumentException>(error);

    [Then("the view offers no wstat or open-stat capability")]
    public void NoMetadataUpdates()
    {
        Assert.Null(error);
        Assert.IsNotAssignableFrom<IResourceWStatOperations>(result);
        Assert.IsNotAssignableFrom<IResourceOpenStatOperations>(result);
    }

    [Given(@"^a policy with (.*)$")]
    public void PolicyWithDefect(string defect)
    {
        var grant = new ResourceGrant(GrantSubjectKind.User, "alice", tree.Handle("/data").Identity, GrantScope.Self, ResourceRights.Read);
        pendingPolicy = defect switch
        {
            "generation 0" => () => new AuthorizationPolicy(0, principals, memberships, new[] { grant }),
            "a grant with no rights" => () => new AuthorizationPolicy(1, principals, memberships, new[] { grant with { Rights = ResourceRights.None } }),
            "the same grant twice" => () => new AuthorizationPolicy(1, principals, memberships, new[] { grant, grant }),
            "a group member who is not a principal" => () => new AuthorizationPolicy(1, principals,
                memberships.Append(new GroupMembership("writers", "nobody")), new[] { grant }),
            "two principals with the same user" => () => new AuthorizationPolicy(1,
                principals.Append(new AuthorizationPrincipal("alice", false)), memberships, new[] { grant }),
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect),
        };
    }

    [When("the policy is constructed")]
    public void PolicyConstructed()
    {
        error = null;
        try { result = pendingPolicy!(); }
        catch (Exception caught) { error = caught; }
    }

    [Then("construction fails")]
    public void ConstructionFails() => Assert.IsAssignableFrom<ArgumentException>(error);

    private async Task OpenAs(string user, string path, string mode, string contextUser)
        => await Attempt("open", path, async () =>
            await View(user).OpenAsync(tree.Handle(path), OpenMode(mode), Context(contextUser), CancellationToken.None));

    private async Task Attempt(string kind, string path, Func<Task<object>> operation)
    {
        requestKind = kind;
        requestPath = path;
        callsBefore = tree.CallsMatching($"{kind} {path}");
        error = null;
        result = null;
        try { result = await operation(); }
        catch (Exception caught) { error = caught; }
    }

    private AuthorizedResourceOperations View(string user)
    {
        if (views.TryGetValue(user, out AuthorizedResourceOperations? view)) return view;
        var policy = new AuthorizationPolicy(policyGeneration, principals, memberships, grants);
        view = new AuthorizedResourceOperations(tree, reportParents ? tree : null, policy, user, () => currentGeneration, readOnlyRoots);
        views.Add(user, view);
        return view;
    }

    private ResourceOperationContext Context(string user) => new(new ResourceOperationId("authz", ++sequence), 1, user);

    private static uint Octal(string value) => Convert.ToUInt32(value, 8);

    private static ResourceRights Rights(string value)
        => Enum.Parse<ResourceRights>(value.Replace(",", ", ", StringComparison.Ordinal), ignoreCase: true);

    private static byte OpenMode(string value)
    {
        byte mode = 0;
        foreach (string part in value.Split(','))
        {
            mode |= part switch
            {
                "OREAD" => NinePConstants.OREAD,
                "OWRITE" => NinePConstants.OWRITE,
                "ORDWR" => NinePConstants.ORDWR,
                "OEXEC" => NinePConstants.OEXEC,
                "OTRUNC" => NinePConstants.OTRUNC,
                "ORCLOSE" => NinePConstants.ORCLOSE,
                _ => throw new ArgumentOutOfRangeException(nameof(value), part),
            };
        }

        return mode;
    }
}

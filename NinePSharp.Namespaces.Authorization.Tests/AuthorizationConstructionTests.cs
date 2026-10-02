using NinePSharp.Namespaces.Authorization.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Authorization.Tests;

public sealed class AuthorizationConstructionTests
{
    private static readonly AuthorizationPrincipal[] Principals = [new("alice"), new("bob", Enabled: false)];
    private static readonly ResourceIdentity Resource = new("tree", "disk", 1);
    private static readonly ResourceGrant Grant = new(GrantSubjectKind.User, "alice", Resource, GrantScope.Self, ResourceRights.Read);

    [Fact]
    public void PolicyRejectsMissingCollectionsAndElements()
    {
        Assert.Equal("principals", Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, null!, [], [])).ParamName);
        Assert.Equal("memberships", Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, Principals, null!, [])).ParamName);
        Assert.Equal("grants", Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, Principals, [], null!)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, [null!], [], []));
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, Principals, [null!], []));
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, Principals, [], [null!]));
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Resource = null! }]));
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, [new AuthorizationPrincipal(" ")], [], []));
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [new GroupMembership(" ", "alice")], []));
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Subject = " " }]));
        Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(
            1,
            Principals,
            [],
            [Grant with { SubjectKind = GrantSubjectKind.Group, Subject = " " }]));
    }

    [Fact]
    public void PolicyValidationMessagesNameTheDefect()
    {
        Assert.StartsWith(
            "Policy generations start at one.",
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuthorizationPolicy(0, Principals, [], [])).Message);
        Assert.StartsWith(
            "Principal 'alice' is listed twice.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, [.. Principals, new("alice")], [], [])).Message);
        Assert.StartsWith(
            "Group member 'carol' is not a principal.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [new("staff", "carol")], [])).Message);
        Assert.StartsWith(
            "Membership of 'alice' in 'staff' is listed twice.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [new("staff", "alice"), new("staff", "alice")], [])).Message);
        Assert.StartsWith(
            "A grant has an undefined subject kind or scope.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { SubjectKind = (GrantSubjectKind)9 }])).Message);
        Assert.StartsWith(
            "A grant has an undefined subject kind or scope.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Scope = (GrantScope)9 }])).Message);
        Assert.StartsWith(
            "A grant must confer at least one defined right.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Rights = ResourceRights.None }])).Message);
        Assert.StartsWith(
            "A grant must confer at least one defined right.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Rights = (ResourceRights)64 }])).Message);
        Assert.StartsWith(
            "Grant subject 'carol' is not a principal.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant with { Subject = "carol" }])).Message);
        Assert.StartsWith(
            "A grant is listed twice.",
            Assert.Throws<ArgumentException>(() => new AuthorizationPolicy(1, Principals, [], [Grant, Grant])).Message);
    }

    [Fact]
    public void PolicyExposesItsValidatedGenerationGrantsAndPrincipals()
    {
        var groupGrant = new ResourceGrant(GrantSubjectKind.Group, "carol", Resource, GrantScope.Tree, ResourceRights.All);
        var policy = new AuthorizationPolicy(7, Principals, [], [Grant, groupGrant]);
        Assert.Equal(7UL, policy.Generation);
        Assert.Equal(new[] { Grant, groupGrant }, policy.Grants);
        Assert.True(policy.IsEnabled("alice"));
        Assert.False(policy.IsEnabled("bob"));
        Assert.False(policy.IsEnabled("carol"));
        Assert.True(policy.Contains("bob"));
        Assert.False(policy.Contains("carol"));
        Assert.Equal(new[] { Grant }, policy.GrantsFor("alice"));
        Assert.Empty(policy.GrantsFor("bob"));
    }

    [Fact]
    public void ViewRejectsMissingDependenciesAndUnknownPrincipals()
    {
        var tree = new TreeResources();
        var policy = new AuthorizationPolicy(1, Principals, [], [Grant]);
        Func<ulong> generation = () => 1;
        Assert.Equal("inner", Assert.Throws<ArgumentNullException>(() => new AuthorizedResourceOperations(null!, tree, policy, "alice", generation)).ParamName);
        Assert.Equal("policy", Assert.Throws<ArgumentNullException>(() => new AuthorizedResourceOperations(tree, tree, null!, "alice", generation)).ParamName);
        Assert.Equal("currentGeneration", Assert.Throws<ArgumentNullException>(() => new AuthorizedResourceOperations(tree, tree, policy, "alice", null!)).ParamName);
        Assert.Throws<ArgumentException>(() => new AuthorizedResourceOperations(tree, tree, policy, " ", generation));
        Assert.StartsWith(
            "'carol' is not a principal of this policy.",
            Assert.Throws<ArgumentException>(() => new AuthorizedResourceOperations(tree, tree, policy, "carol", generation)).Message);
        _ = new AuthorizedResourceOperations(tree, null, policy, "alice", generation);
        Assert.StartsWith(
            "Tree grants and read-only roots need the provider's parent relation.",
            Assert.Throws<ArgumentException>(() => new AuthorizedResourceOperations(tree, null, policy, "alice", generation, [Resource])).Message);
    }

    [Fact]
    public void DenialsUseThePlan9PermissionMessage()
        => Assert.Equal("permission denied", new ResourceAccessDeniedException().Message);
}

namespace NinePSharp.Namespaces.Authorization;

/// <summary>An immutable, validated policy generation: principals, group membership and grants.</summary>
public sealed class AuthorizationPolicy
{
    private readonly Dictionary<string, AuthorizationPrincipal> principals;
    private readonly HashSet<(string Group, string Member)> memberships;

    /// <summary>Initializes a new instance of the <see cref="AuthorizationPolicy"/> class. The policy generation is validated and frozen.</summary>
    public AuthorizationPolicy(
        ulong generation,
        IEnumerable<AuthorizationPrincipal> principals,
        IEnumerable<GroupMembership> memberships,
        IEnumerable<ResourceGrant> grants)
    {
        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Policy generations start at one.");
        }

        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(memberships);
        ArgumentNullException.ThrowIfNull(grants);

        this.principals = new Dictionary<string, AuthorizationPrincipal>(StringComparer.Ordinal);
        foreach (AuthorizationPrincipal principal in principals)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentException.ThrowIfNullOrWhiteSpace(principal.User);
            if (!this.principals.TryAdd(principal.User, principal))
            {
                throw new ArgumentException($"Principal '{principal.User}' is listed twice.", nameof(principals));
            }
        }

        this.memberships = new HashSet<(string, string)>();
        foreach (GroupMembership membership in memberships)
        {
            ArgumentNullException.ThrowIfNull(membership);
            ArgumentException.ThrowIfNullOrWhiteSpace(membership.Group);
            if (!this.principals.ContainsKey(membership.Member))
            {
                throw new ArgumentException($"Group member '{membership.Member}' is not a principal.", nameof(memberships));
            }

            if (!this.memberships.Add((membership.Group, membership.Member)))
            {
                throw new ArgumentException($"Membership of '{membership.Member}' in '{membership.Group}' is listed twice.", nameof(memberships));
            }
        }

        var accepted = new HashSet<ResourceGrant>();
        foreach (ResourceGrant grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.Subject);
            ArgumentNullException.ThrowIfNull(grant.Resource);
            if (!Enum.IsDefined(grant.SubjectKind) || !Enum.IsDefined(grant.Scope))
            {
                throw new ArgumentException("A grant has an undefined subject kind or scope.", nameof(grants));
            }

            if (grant.Rights == ResourceRights.None || (grant.Rights & ~ResourceRights.All) != 0)
            {
                throw new ArgumentException("A grant must confer at least one defined right.", nameof(grants));
            }

            if (grant.SubjectKind == GrantSubjectKind.User && !this.principals.ContainsKey(grant.Subject))
            {
                throw new ArgumentException($"Grant subject '{grant.Subject}' is not a principal.", nameof(grants));
            }

            if (!accepted.Add(grant))
            {
                throw new ArgumentException("A grant is listed twice.", nameof(grants));
            }
        }

        Generation = generation;
        Grants = accepted.ToArray();
    }

    /// <summary>Gets the policy generation; a different current generation revokes this policy's authority.</summary>
    public ulong Generation { get; }

    /// <summary>Gets the validated grants.</summary>
    public IReadOnlyList<ResourceGrant> Grants { get; }

    /// <summary>Returns whether the user is a known, enabled principal.</summary>
    public bool IsEnabled(string user) => principals.TryGetValue(user, out AuthorizationPrincipal? principal) && principal.Enabled;

    /// <summary>Returns whether the user is a known principal.</summary>
    public bool Contains(string user) => principals.ContainsKey(user);

    /// <summary>
    /// 9front gefs and hjfs ingroup: a user is a member of the group with its own name and of
    /// each group listing it explicitly. Membership is not recursive.
    /// </summary>
    public bool InGroup(string user, string group)
        => string.Equals(user, group, StringComparison.Ordinal) || memberships.Contains((group, user));

    /// <summary>Returns the grants whose subject includes the user.</summary>
    public IReadOnlyList<ResourceGrant> GrantsFor(string user)
        => Grants.Where(grant => grant.SubjectKind == GrantSubjectKind.User
                ? string.Equals(grant.Subject, user, StringComparison.Ordinal)
                : InGroup(user, grant.Subject))
            .ToArray();
}

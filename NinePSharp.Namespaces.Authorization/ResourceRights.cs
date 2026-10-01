namespace NinePSharp.Namespaces.Authorization;

/// <summary>Resource rights a grant can confer, in NamespacePolicy.md's canonical order.</summary>
[Flags]
public enum ResourceRights
{
    /// <summary>No rights.</summary>
    None = 0,

    /// <summary>Read the resource's metadata.</summary>
    Stat = 1,

    /// <summary>Walk out of a directory.</summary>
    Walk = 2,

    /// <summary>Read data, list a directory, or execute.</summary>
    Read = 4,

    /// <summary>Write or truncate data.</summary>
    Write = 8,

    /// <summary>Create children in a directory.</summary>
    Create = 16,

    /// <summary>Remove the resource, directly or on close.</summary>
    Remove = 32,

    /// <summary>Every right.</summary>
    All = Stat | Walk | Read | Write | Create | Remove,
}

/// <summary>What a grant covers.</summary>
public enum GrantScope
{
    /// <summary>Exactly the named resource.</summary>
    Self,

    /// <summary>The named resource and its provider-attested descendants.</summary>
    Tree,
}

/// <summary>Whom a grant names.</summary>
public enum GrantSubjectKind
{
    /// <summary>One principal.</summary>
    User,

    /// <summary>Every member of a group, as 9front gefs ingroup defines membership.</summary>
    Group,
}

/// <summary>Allows a user or group the given rights on a resource identity.</summary>
/// <param name="SubjectKind">Whether <paramref name="Subject"/> names a user or a group.</param>
/// <param name="Subject">The user or group name.</param>
/// <param name="Resource">The stable (provider, device, path) identity, never a visible pathname.</param>
/// <param name="Scope">Whether the grant covers only the resource or also its descendants.</param>
/// <param name="Rights">The rights allowed; never empty.</param>
public sealed record ResourceGrant(
    GrantSubjectKind SubjectKind,
    string Subject,
    ResourceIdentity Resource,
    GrantScope Scope,
    ResourceRights Rights);

/// <summary>An authenticated identity known to the policy.</summary>
/// <param name="User">The principal's user name.</param>
/// <param name="Enabled">Whether the principal may perform any operation.</param>
public sealed record AuthorizationPrincipal(string User, bool Enabled = true);

/// <summary>States that a principal is an explicit member of a group.</summary>
/// <param name="Group">The group name.</param>
/// <param name="Member">The member's user name.</param>
public sealed record GroupMembership(string Group, string Member);

/// <summary>A definite authorization denial. The operation was not dispatched to its provider.</summary>
public sealed class ResourceAccessDeniedException : UnauthorizedAccessException
{
    /// <summary>Initializes a denial.</summary>
    public ResourceAccessDeniedException() : base("permission denied") { }
}

/// <summary>
/// Provider-attested parent relation. Tree grants, read-only roots, remove and remove-on-close
/// need it; string prefixes and caller-supplied parents never establish containment.
/// </summary>
public interface IResourceAncestry
{
    /// <summary>Returns the resource's current parent directory, or null at a provider root.</summary>
    ValueTask<ResourceHandle?> GetParentAsync(ResourceHandle resource, CancellationToken cancellationToken);
}

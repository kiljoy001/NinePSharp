namespace NinePSharp.Namespaces.Authorization;

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

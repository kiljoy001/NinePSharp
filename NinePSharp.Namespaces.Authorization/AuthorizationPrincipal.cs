namespace NinePSharp.Namespaces.Authorization;

/// <summary>An authenticated identity known to the policy.</summary>
/// <param name="User">The principal's user name.</param>
/// <param name="Enabled">Whether the principal may perform any operation.</param>
public sealed record AuthorizationPrincipal(string User, bool Enabled = true);

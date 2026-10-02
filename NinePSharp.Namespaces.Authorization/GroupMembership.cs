namespace NinePSharp.Namespaces.Authorization;

/// <summary>States that a principal is an explicit member of a group.</summary>
/// <param name="Group">The group name.</param>
/// <param name="Member">The member's user name.</param>
public sealed record GroupMembership(string Group, string Member);

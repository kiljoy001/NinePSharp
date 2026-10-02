namespace NinePSharp.Namespaces.Authorization;

/// <summary>Whom a grant names.</summary>
public enum GrantSubjectKind
{
    /// <summary>One principal.</summary>
    User,

    /// <summary>Every member of a group, as 9front gefs ingroup defines membership.</summary>
    Group,
}

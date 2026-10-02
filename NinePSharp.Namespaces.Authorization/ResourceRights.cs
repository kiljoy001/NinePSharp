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

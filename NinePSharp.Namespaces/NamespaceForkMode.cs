namespace NinePSharp.Namespaces;

/// <summary>Controls namespace ownership when a virtual process is forked.</summary>
public enum NamespaceForkMode
{
    /// <summary>The child shares its parent's virtual process group.</summary>
    Share,

    /// <summary>The child receives an independent snapshot of the parent's namespace.</summary>
    Copy,

    /// <summary>The child receives a new empty namespace.</summary>
    Empty,
}

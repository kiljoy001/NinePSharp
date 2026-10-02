using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Controls namespace ownership when an Orleans vProcess is forked.</summary>
public enum NamespaceForkModeModel
{
    /// <summary>Share the parent's vProcess group.</summary>
    Share,

    /// <summary>Clone the parent's vProcess group.</summary>
    Copy,

    /// <summary>Create an empty vProcess group.</summary>
    Empty,
}

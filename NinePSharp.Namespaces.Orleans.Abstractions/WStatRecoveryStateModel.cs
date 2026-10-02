using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>The durable resolution state of an admitted metadata mutation.</summary>
public enum WStatRecoveryStateModel
{
    /// <summary>The request may or may not have reached its provider.</summary>
    Pending,

    /// <summary>The provider result has been durably recorded.</summary>
    Committed,

    /// <summary>The provider definitively rejected the request without applying it.</summary>
    Rejected,
}

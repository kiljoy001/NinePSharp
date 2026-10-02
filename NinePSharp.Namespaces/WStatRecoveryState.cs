using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>The durable resolution state of one admitted metadata mutation.</summary>
public enum WStatRecoveryState
{
    /// <summary>The request may or may not have reached its provider.</summary>
    Pending,

    /// <summary>The provider acknowledged the mutation and its result is durable.</summary>
    Committed,

    /// <summary>The provider definitively rejected the mutation without applying it.</summary>
    Rejected,
}

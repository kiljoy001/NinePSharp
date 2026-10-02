using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>An admitted mutation whose provider outcome must be reconciled under its original identity.</summary>
public sealed class WStatRecoveryPendingException(ResourceOperationContext context, Exception inner)
    : IOException("wstat outcome is unknown; reconcile the original operation before retrying", inner)
{
    /// <summary>Gets the identity and principal of the unresolved operation.</summary>
    public ResourceOperationContext Context { get; } = context;
}

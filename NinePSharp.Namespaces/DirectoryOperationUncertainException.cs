using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

/// <summary>An unresolved directory operation retaining the identity required for provider reconciliation.</summary>
public sealed class DirectoryOperationUncertainException(ResourceOperationContext context, Exception inner)
    : IOException("directory member open outcome is unknown; resolve the original operation before retrying", inner)
{
    /// <summary>Gets the original operation identity; replay must never allocate a new one.</summary>
    public ResourceOperationContext Context { get; } = context;
}

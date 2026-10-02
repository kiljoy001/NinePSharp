using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>The result of a potentially partial multi-element walk.</summary>
/// <param name="Channel">The channel at the last successful element.</param>
/// <param name="Qids">Qids for successful path elements.</param>
public sealed record NamespaceWalkResult(NamespaceChannel Channel, IReadOnlyList<Qid> Qids)
{
    /// <summary>Gets whether every requested element was walked.</summary>
    public bool Complete(int requestedElements) => Qids.Count == requestedElements;
}

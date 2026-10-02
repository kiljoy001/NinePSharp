namespace NinePSharp.Namespaces;

/// <summary>Reports a failed namespace operation without depending on a wire dialect.</summary>
public sealed class NamespaceException : Exception
{
    public NamespaceException(NamespaceError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>Gets the stable error classification.</summary>
    public NamespaceError Error { get; }
}

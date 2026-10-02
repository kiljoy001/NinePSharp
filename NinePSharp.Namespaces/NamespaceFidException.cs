using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Thrown when a 9P fid operation violates protocol state.</summary>
public sealed class NamespaceFidException : InvalidOperationException
{
    public NamespaceFidException(string message)
        : base(message)
    {
    }
}

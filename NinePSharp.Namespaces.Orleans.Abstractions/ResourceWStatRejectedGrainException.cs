namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable definite wstat rejection which guarantees no mutation was applied.</summary>
[GenerateSerializer]
public sealed class ResourceWStatRejectedGrainException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ResourceWStatRejectedGrainException"/> class.</summary>
    public ResourceWStatRejectedGrainException(string message)
        : base(message)
    {
    }
}

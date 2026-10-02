namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable, acknowledged resource-grain create rejection, never a lost reply.</summary>
[GenerateSerializer]
public sealed class ResourceCreateRejectedGrainException : Exception
{
    public ResourceCreateRejectedGrainException(string message)
        : base(message)
    {
    }
}

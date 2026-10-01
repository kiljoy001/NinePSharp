namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable, acknowledged resource-grain create rejection, never a lost reply.</summary>
[GenerateSerializer]
public sealed class ResourceCreateRejectedGrainException : Exception
{
    /// <summary>Initializes the provider's definite rejection.</summary>
    public ResourceCreateRejectedGrainException(string message) : base(message) { }
}

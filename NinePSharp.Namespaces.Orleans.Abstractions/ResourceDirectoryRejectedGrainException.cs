namespace NinePSharp.Namespaces.Orleans;

/// <summary>A definite directory operation rejection, never a timeout or lost reply.</summary>
[GenerateSerializer]
public sealed class ResourceDirectoryRejectedGrainException(string message) : Exception(message);

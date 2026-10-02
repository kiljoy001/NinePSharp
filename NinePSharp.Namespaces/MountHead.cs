namespace NinePSharp.Namespaces;

/// <summary>A mounted-upon resource and its ordered replacement or union members.</summary>
/// <param name="From">The resource being mounted upon.</param>
/// <param name="Mounts">The ordered mounted resources.</param>
public sealed record MountHead(ResourceHandle From, IReadOnlyList<MountBinding> Mounts);

using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable mount head.</summary>
[GenerateSerializer]
public sealed record MountHeadModel(
    [property: Id(0)] ResourceHandleModel From,
    [property: Id(1)] MountBindingModel[] Mounts);

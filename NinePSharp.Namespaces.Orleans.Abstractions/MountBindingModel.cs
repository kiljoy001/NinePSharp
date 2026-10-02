using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable ordered mount member.</summary>
[GenerateSerializer]
public sealed record MountBindingModel(
    [property: Id(0)] long MountId,
    [property: Id(1)] MountFlags Flags,
    [property: Id(2)] ResourceHandleModel Target,
    [property: Id(3)] string Spec);

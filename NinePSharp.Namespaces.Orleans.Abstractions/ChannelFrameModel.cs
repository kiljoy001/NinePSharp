using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable channel traversal frame.</summary>
[GenerateSerializer]
public sealed record ChannelFrameModel(
    [property: Id(0)] string Name,
    [property: Id(1)] ResourceHandleModel Handle,
    [property: Id(2)] ResourceHandleModel? MountedFrom,
    [property: Id(3)] MountBindingModel[]? Union);

using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable named resource.</summary>
[GenerateSerializer]
public sealed record ResourceDirectoryEntryModel(
    [property: Id(0)] string Name,
    [property: Id(1)] ResourceHandleModel Handle);

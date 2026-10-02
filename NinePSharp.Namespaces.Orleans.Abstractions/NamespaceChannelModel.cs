using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable channel retaining mount-crossing history.</summary>
[GenerateSerializer]
public sealed record NamespaceChannelModel([property: Id(0)] ChannelFrameModel[] Frames);

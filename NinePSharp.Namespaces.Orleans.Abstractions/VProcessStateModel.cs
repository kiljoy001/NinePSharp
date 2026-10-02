using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>The durable state exposed by a virtual process grain.</summary>
[GenerateSerializer]
public sealed record VProcessStateModel(
    [property: Id(0)] long ProcessId,
    [property: Id(1)] long? ParentId,
    [property: Id(2)] string ProcessGroupId,
    [property: Id(3)] NamespaceChannelModel Root,
    [property: Id(4)] NamespaceChannelModel CurrentDirectory);

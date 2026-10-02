using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable process-group namespace.</summary>
[GenerateSerializer]
public sealed record NamespaceSnapshotModel(
    [property: Id(0)] long NextMountId,
    [property: Id(1)] MountHeadModel[] MountHeads,
    [property: Id(2)] bool MountsDisabled = false,
    [property: Id(3)] string[]? BlockedMountDevices = null);

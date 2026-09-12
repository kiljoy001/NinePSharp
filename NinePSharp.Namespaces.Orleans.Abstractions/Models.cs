using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>A serializable stable resource identity.</summary>
[GenerateSerializer]
public sealed record ResourceIdentityModel(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Device,
    [property: Id(2)] ulong Path);

/// <summary>A serializable resource handle.</summary>
[GenerateSerializer]
public sealed record ResourceHandleModel(
    [property: Id(0)] ResourceIdentityModel Identity,
    [property: Id(1)] QidType Type,
    [property: Id(2)] uint Version);

/// <summary>A serializable named resource.</summary>
[GenerateSerializer]
public sealed record ResourceDirectoryEntryModel(
    [property: Id(0)] string Name,
    [property: Id(1)] ResourceHandleModel Handle);

/// <summary>A serializable identity for one replayable resource operation.</summary>
[GenerateSerializer]
public sealed record ResourceOperationIdModel(
    [property: Id(0)] string SessionId,
    [property: Id(1)] ulong Sequence);

/// <summary>Serializable authenticated process context for a resource operation.</summary>
[GenerateSerializer]
public sealed record ResourceOperationContextModel(
    [property: Id(0)] ResourceOperationIdModel OperationId,
    [property: Id(1)] long ProcessId,
    [property: Id(2)] string User);

/// <summary>Serializable provider-owned state associated with an open fid.</summary>
[GenerateSerializer]
public sealed record ResourceOpenHandleModel(
    [property: Id(0)] ResourceHandleModel Resource,
    [property: Id(1)] string HandleId,
    [property: Id(2)] byte Mode,
    [property: Id(3)] uint IoUnit);

/// <summary>Serializable provider-neutral metadata for a 9P stat response.</summary>
[GenerateSerializer]
public sealed record ResourceStatModel(
    [property: Id(0)] ResourceHandleModel Resource,
    [property: Id(1)] string Name,
    [property: Id(2)] uint Mode,
    [property: Id(3)] uint AccessTime,
    [property: Id(4)] uint ModificationTime,
    [property: Id(5)] ulong Length,
    [property: Id(6)] string User,
    [property: Id(7)] string Group,
    [property: Id(8)] string LastModifier);

/// <summary>A serializable ordered mount member.</summary>
[GenerateSerializer]
public sealed record MountBindingModel(
    [property: Id(0)] long MountId,
    [property: Id(1)] MountFlags Flags,
    [property: Id(2)] ResourceHandleModel Target,
    [property: Id(3)] string Spec);

/// <summary>A serializable mount head.</summary>
[GenerateSerializer]
public sealed record MountHeadModel(
    [property: Id(0)] ResourceHandleModel From,
    [property: Id(1)] MountBindingModel[] Mounts);

/// <summary>A serializable process-group namespace.</summary>
[GenerateSerializer]
public sealed record NamespaceSnapshotModel(
    [property: Id(0)] long NextMountId,
    [property: Id(1)] MountHeadModel[] MountHeads,
    [property: Id(2)] bool MountsDisabled = false,
    [property: Id(3)] string[]? BlockedMountDevices = null);

/// <summary>A serializable channel traversal frame.</summary>
[GenerateSerializer]
public sealed record ChannelFrameModel(
    [property: Id(0)] string Name,
    [property: Id(1)] ResourceHandleModel Handle,
    [property: Id(2)] ResourceHandleModel? MountedFrom,
    [property: Id(3)] MountBindingModel[]? Union);

/// <summary>A serializable channel retaining mount-crossing history.</summary>
[GenerateSerializer]
public sealed record NamespaceChannelModel([property: Id(0)] ChannelFrameModel[] Frames);

/// <summary>Controls namespace ownership when an Orleans vProcess is forked.</summary>
public enum NamespaceForkModeModel
{
    /// <summary>Share the parent's vProcess group.</summary>
    Share,

    /// <summary>Clone the parent's vProcess group.</summary>
    Copy,

    /// <summary>Create an empty vProcess group.</summary>
    Empty,
}

/// <summary>The durable state exposed by a virtual process grain.</summary>
[GenerateSerializer]
public sealed record VProcessStateModel(
    [property: Id(0)] long ProcessId,
    [property: Id(1)] long? ParentId,
    [property: Id(2)] string ProcessGroupId,
    [property: Id(3)] NamespaceChannelModel Root,
    [property: Id(4)] NamespaceChannelModel CurrentDirectory);

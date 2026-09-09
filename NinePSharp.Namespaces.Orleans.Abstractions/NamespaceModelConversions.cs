namespace NinePSharp.Namespaces.Orleans;

/// <summary>Converts pure namespace values to and from Orleans wire models.</summary>
public static class NamespaceModelConversions
{
    /// <summary>Converts a resource identity to its wire model.</summary>
    public static ResourceIdentityModel ToModel(this ResourceIdentity value)
        => new(value.Provider, value.Device, value.Path);

    /// <summary>Converts a resource identity from its wire model.</summary>
    public static ResourceIdentity ToDomain(this ResourceIdentityModel value)
        => new(value.Provider, value.Device, value.Path);

    /// <summary>Converts a resource handle to its wire model.</summary>
    public static ResourceHandleModel ToModel(this ResourceHandle value)
        => new(value.Identity.ToModel(), value.Type, value.Version);

    /// <summary>Converts a resource handle from its wire model.</summary>
    public static ResourceHandle ToDomain(this ResourceHandleModel value)
        => new(value.Identity.ToDomain(), value.Type, value.Version);

    /// <summary>Converts a mount binding to its wire model.</summary>
    public static MountBindingModel ToModel(this MountBinding value)
        => new(value.MountId, value.Flags, value.Target.ToModel(), value.Spec);

    /// <summary>Converts a mount binding from its wire model.</summary>
    public static MountBinding ToDomain(this MountBindingModel value)
        => new(value.MountId, value.Flags, value.Target.ToDomain(), value.Spec);

    /// <summary>Converts a namespace snapshot to its wire model.</summary>
    public static NamespaceSnapshotModel ToModel(this NamespaceSnapshot value)
        => new(
            value.NextMountId,
            value.MountHeads.Select(
                head => new MountHeadModel(head.From.ToModel(), head.Mounts.Select(ToModel).ToArray())).ToArray());

    /// <summary>Converts a namespace snapshot from its wire model.</summary>
    public static NamespaceSnapshot ToDomain(this NamespaceSnapshotModel value)
        => new(
            value.NextMountId,
            value.MountHeads.Select(
                head => new MountHead(head.From.ToDomain(), head.Mounts.Select(ToDomain).ToArray())).ToArray());

    /// <summary>Converts a namespace channel to its wire model.</summary>
    public static NamespaceChannelModel ToModel(this NamespaceChannel value)
        => new(value.Frames.Select(ToModel).ToArray());

    /// <summary>Converts a namespace channel from its wire model.</summary>
    public static NamespaceChannel ToDomain(this NamespaceChannelModel value)
        => NamespaceChannel.Restore(value.Frames.Select(ToDomain));

    private static ChannelFrameModel ToModel(ChannelFrame value)
        => new(
            value.Name,
            value.Handle.ToModel(),
            value.MountedFrom?.ToModel(),
            value.Union?.Select(ToModel).ToArray());

    private static ChannelFrame ToDomain(ChannelFrameModel value)
        => new(
            value.Name,
            value.Handle.ToDomain(),
            value.MountedFrom?.ToDomain(),
            value.Union?.Select(ToDomain).ToArray());
}

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

    /// <summary>Converts an operation identity to its wire model.</summary>
    public static ResourceOperationIdModel ToModel(this ResourceOperationId value)
        => new(value.SessionId, value.Sequence);

    /// <summary>Converts an operation identity from its wire model.</summary>
    public static ResourceOperationId ToDomain(this ResourceOperationIdModel value)
        => new(value.SessionId, value.Sequence);

    /// <summary>Converts an operation context to its wire model.</summary>
    public static ResourceOperationContextModel ToModel(this ResourceOperationContext value)
        => new(value.OperationId.ToModel(), value.ProcessId, value.User);

    /// <summary>Converts an operation context from its wire model.</summary>
    public static ResourceOperationContext ToDomain(this ResourceOperationContextModel value)
        => new(value.OperationId.ToDomain(), value.ProcessId, value.User);

    /// <summary>Converts an open handle to its wire model.</summary>
    public static ResourceOpenHandleModel ToModel(this ResourceOpenHandle value)
        => new(value.Resource.ToModel(), value.HandleId, value.Mode, value.IoUnit, value.IsMountTransport);

    /// <summary>Converts an open handle from its wire model.</summary>
    public static ResourceOpenHandle ToDomain(this ResourceOpenHandleModel value)
        => new(value.Resource.ToDomain(), value.HandleId, value.Mode, value.IoUnit, value.IsMountTransport);

    /// <summary>Converts resource metadata to its wire model.</summary>
    public static ResourceStatModel ToModel(this ResourceStat value)
        => new(
            value.Resource.ToModel(),
            value.Name,
            value.Mode,
            value.AccessTime,
            value.ModificationTime,
            value.Length,
            value.User,
            value.Group,
            value.LastModifier);

    /// <summary>Converts resource metadata from its wire model.</summary>
    public static ResourceStat ToDomain(this ResourceStatModel value)
        => new(
            value.Resource.ToDomain(),
            value.Name,
            value.Mode,
            value.AccessTime,
            value.ModificationTime,
            value.Length,
            value.User,
            value.Group,
            value.LastModifier);

    /// <summary>Converts a metadata update to its wire model without changing sentinels.</summary>
    public static ResourceWStatModel ToModel(this ResourceWStat value)
        => new(
            value.Type,
            value.Device,
            value.Qid.Type,
            value.Qid.Version,
            value.Qid.Path,
            value.Mode,
            value.AccessTime,
            value.ModificationTime,
            value.Length,
            value.Name,
            value.User,
            value.Group,
            value.LastModifier,
            value.EncodedLength);

    /// <summary>Converts a metadata update from its wire model without changing sentinels.</summary>
    public static ResourceWStat ToDomain(this ResourceWStatModel value)
        => new(
            value.Type,
            value.Device,
            new NinePSharp.Constants.Qid(value.QidType, value.QidVersion, value.QidPath),
            value.Mode,
            value.AccessTime,
            value.ModificationTime,
            value.Length,
            value.Name,
            value.User,
            value.Group,
            value.LastModifier,
            value.EncodedLength);

    /// <summary>Converts a saved wstat request to its wire model.</summary>
    public static WStatRecoveryRequestModel ToModel(this WStatRecoveryRequest value)
        => new(
            value.Context.ToModel(),
            value.Resource.ToModel(),
            value.OpenHandle?.ToModel(),
            value.Stat.ToArray(),
            value.Fingerprint);

    /// <summary>Converts a saved wstat request from its wire model and verifies its fingerprint.</summary>
    public static WStatRecoveryRequest ToDomain(this WStatRecoveryRequestModel value)
    {
        if (value.OpenHandle is not null && value.OpenHandle.Resource != value.Resource)
            throw new InvalidDataException("The durable wstat open handle identifies a different resource.");
        WStatRecoveryRequest request = value.OpenHandle is null
            ? WStatRecoveryRequest.ForResource(value.Resource.ToDomain(), value.Stat, value.Context.ToDomain())
            : WStatRecoveryRequest.ForOpenHandle(value.OpenHandle.ToDomain(), value.Stat, value.Context.ToDomain());
        if (!string.Equals(request.Fingerprint, value.Fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("The durable wstat request fingerprint is invalid.");
        return request;
    }

    /// <summary>Converts a durable wstat journal entry to its wire model.</summary>
    public static WStatRecoveryRecordModel ToModel(this WStatRecoveryRecord value)
        => new(value.Request.ToModel(), (WStatRecoveryStateModel)value.State, value.Result, value.Error);

    /// <summary>Converts a durable wstat journal entry from its wire model.</summary>
    public static WStatRecoveryRecord ToDomain(this WStatRecoveryRecordModel value)
    {
        if (!Enum.IsDefined(value.State))
            throw new InvalidDataException("The durable wstat recovery state is invalid.");
        return new WStatRecoveryRecord(
            value.Request.ToDomain(),
            (WStatRecoveryState)value.State,
            value.Result,
            value.Error);
    }

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
                head => new MountHeadModel(head.From.ToModel(), head.Mounts.Select(ToModel).ToArray())).ToArray(),
            value.MountsDisabled,
            value.BlockedMountDevices?.ToArray());

    /// <summary>Converts a namespace snapshot from its wire model.</summary>
    public static NamespaceSnapshot ToDomain(this NamespaceSnapshotModel value)
        => new(
            value.NextMountId,
            value.MountHeads.Select(
                head => new MountHead(head.From.ToDomain(), head.Mounts.Select(ToDomain).ToArray())).ToArray(),
            value.MountsDisabled,
            value.BlockedMountDevices);

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

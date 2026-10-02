namespace NinePSharp.Namespaces.Orleans;

/// <summary>Owns the durable mount table shared by one or more virtual processes.</summary>
public interface IVProcessGroupGrain : IGrainWithStringKey
{
    /// <summary>Initializes an empty group if it has not been initialized.</summary>
    Task InitializeEmptyAsync();

    /// <summary>Initializes an uninitialized group from a namespace snapshot.</summary>
    Task InitializeFromAsync(NamespaceSnapshotModel snapshot);

    /// <summary>Returns the current namespace snapshot.</summary>
    Task<NamespaceSnapshotModel> GetSnapshotAsync();

    /// <summary>Adds a replacement or union member.</summary>
    Task<MountBindingModel> MountAsync(
        ResourceHandleModel target,
        ResourceHandleModel mountedOn,
        MountFlags flags,
        string? spec = null);

    /// <summary>Binds a channel and copies its mounted union.</summary>
    Task<MountBindingModel> BindAsync(
        NamespaceChannelModel source,
        ResourceHandleModel mountedOn,
        MountFlags flags,
        string? spec = null);

    /// <summary>Removes all mounts or a selected union member.</summary>
    Task UnmountAsync(ResourceHandleModel mountedOn, ResourceHandleModel? mounted = null);

    /// <summary>Enables or disables every service mount in this namespace.</summary>
    Task SetMountsDisabledAsync(bool disabled);

    /// <summary>Blocks or permits service mounts targeting one device name.</summary>
    Task SetMountDeviceBlockedAsync(string device, bool blocked);

    /// <summary>Finds a mount head by stable object identity.</summary>
    Task<MountHeadModel?> FindMountAsync(ResourceIdentityModel identity);

    /// <summary>Initializes another process group with an independent namespace clone.</summary>
    Task CloneToAsync(string destinationProcessGroupId);
}

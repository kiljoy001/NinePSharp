namespace NinePSharp.Namespaces.Orleans;

/// <summary>Optional resource capability for metadata on an already retained open instance.</summary>
public interface IOpenStatResourceGrain : IMountableResourceGrain
{
    /// <summary>Stats the given open handle without pathname lookup or consuming it.</summary>
    Task<ResourceStatModel> StatOpenAsync(ResourceOpenHandleModel handle);
}

/// <summary>
/// Optional resource capability: the provider's own parent relation, for authorization layers
/// that must prove containment rather than trust a pathname.
/// </summary>
public interface IAncestryResourceGrain : IMountableResourceGrain
{
    /// <summary>Returns the resource's current parent directory, or null at the provider's root.</summary>
    Task<ResourceHandleModel?> GetParentAsync(ResourceHandleModel resource);
}

/// <summary>Optional resource capability for atomic path and retained-handle metadata updates.</summary>
public interface IWStatResourceGrain : IOpenStatResourceGrain
{
    /// <summary>Applies one exact-width update to a resolved resource.</summary>
    Task<uint> WStatAsync(ResourceHandleModel resource, ResourceWStatModel stat,
        ResourceOperationContextModel context);

    /// <summary>Applies one exact-width update through a retained open handle.</summary>
    Task<uint> WStatOpenAsync(ResourceOpenHandleModel handle, ResourceWStatModel stat,
        ResourceOperationContextModel context);
}

/// <summary>Owns the durable wstat recovery journal for one session epoch.</summary>
public interface IWStatRecoveryJournalGrain : IGrainWithStringKey
{
    /// <summary>Durably admits a request or returns its matching existing entry.</summary>
    Task<WStatRecoveryRecordModel> BeginAsync(WStatRecoveryRequestModel request);

    /// <summary>Returns one admitted operation, or null when it does not exist.</summary>
    Task<WStatRecoveryRecordModel?> GetAsync(ulong sequence);

    /// <summary>Returns unresolved entries in operation order.</summary>
    Task<WStatRecoveryRecordModel[]> GetPendingAsync();

    /// <summary>Records a provider result for the matching admitted request.</summary>
    Task CommitAsync(ulong sequence, string fingerprint, uint result);

    /// <summary>Records a definite provider rejection for the matching admitted request.</summary>
    Task RejectAsync(ulong sequence, string fingerprint, string error);
}

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

/// <summary>Owns durable root/current channels and a reference to a vProcess group.</summary>
public interface IVProcessGrain : IGrainWithIntegerKey
{
    /// <summary>Initializes a virtual process exactly once.</summary>
    Task InitializeAsync(VProcessStateModel initialState);

    /// <summary>Returns the virtual process state.</summary>
    Task<VProcessStateModel> GetStateAsync();

    /// <summary>Changes the current directory channel.</summary>
    Task ChangeDirectoryAsync(NamespaceChannelModel currentDirectory);

    /// <summary>Creates a child with shared, copied, or empty namespace ownership.</summary>
    Task<VProcessStateModel> ForkAsync(long childProcessId, NamespaceForkModeModel mode, bool noMounts = false);

    /// <summary>Changes this process's namespace group without creating a child.</summary>
    Task<VProcessStateModel> RforkNamespaceAsync(NamespaceForkModeModel mode, bool noMounts = false);
}

/// <summary>A grain exposing a mountable 9P-like resource tree.</summary>
public interface IMountableResourceGrain : IGrainWithStringKey
{
    /// <summary>Walks one child name or returns null if it is absent.</summary>
    Task<ResourceHandleModel?> WalkAsync(ResourceHandleModel directory, string name);

    /// <summary>Reads all entries in one directory.</summary>
    Task<ResourceDirectoryEntryModel[]> ReadDirectoryAsync(ResourceHandleModel directory);

    /// <summary>Creates one child in a directory.</summary>
    Task<ResourceHandleModel> CreateAsync(ResourceHandleModel directory, string name, bool directoryEntry);

    /// <summary>Opens a resource. Replaying the same operation must return the same handle.</summary>
    Task<ResourceOpenHandleModel> OpenAsync(
        ResourceHandleModel resource,
        byte mode,
        ResourceOperationContextModel context);

    /// <summary>Reads bytes from an open resource.</summary>
    Task<byte[]> ReadAsync(ResourceOpenHandleModel openHandle, ulong offset, uint count);

    /// <summary>Writes bytes atomically with its idempotency record.</summary>
    Task<uint> WriteAsync(
        ResourceOpenHandleModel openHandle,
        ulong offset,
        byte[] data,
        ResourceOperationContextModel context);

    /// <summary>Reads metadata for a resource.</summary>
    Task<ResourceStatModel> StatAsync(ResourceHandleModel resource);

    /// <summary>
    /// Creates an absent child atomically with its idempotency record; existing names
    /// fail without truncation. A definite rejection uses ResourceCreateRejectedGrainException.
    /// Transport/storage uncertainty must not be reported as a definite rejection.
    /// Replaying a completed create returns its original handle.
    /// </summary>
    Task<ResourceOpenHandleModel> CreateAndOpenAsync(
        ResourceHandleModel directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContextModel context);

    /// <summary>Closes an open handle. Replaying the same operation has no additional effect.</summary>
    Task ClunkAsync(ResourceOpenHandleModel openHandle, ResourceOperationContextModel context);

    /// <summary>Removes a resource atomically with its idempotency record.</summary>
    Task RemoveAsync(
        ResourceHandleModel resource,
        ResourceOpenHandleModel? openHandle,
        ResourceOperationContextModel context);
}

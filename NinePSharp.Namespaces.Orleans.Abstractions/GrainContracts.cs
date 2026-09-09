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
    Task<VProcessStateModel> ForkAsync(long childProcessId, NamespaceForkModeModel mode);
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
}

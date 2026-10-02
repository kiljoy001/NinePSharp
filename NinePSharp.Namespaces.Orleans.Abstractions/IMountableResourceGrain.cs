namespace NinePSharp.Namespaces.Orleans;

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

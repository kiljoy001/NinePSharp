using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Routes operations to the provider named by a resource handle.</summary>
public interface IResourceOperations
{
    /// <summary>Walks one path component from a directory.</summary>
    ValueTask<ResourceHandle?> WalkAsync(ResourceHandle directory, string name, CancellationToken cancellationToken);

    /// <summary>Reads all entries from a directory.</summary>
    ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken);

    /// <summary>Creates a child in a directory.</summary>
    ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken);
}

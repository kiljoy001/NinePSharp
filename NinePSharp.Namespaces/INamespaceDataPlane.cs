namespace NinePSharp.Namespaces;

/// <summary>Namespace and resource operations needed by one 9P connection.</summary>
public interface INamespaceDataPlane
{
    /// <summary>Attaches and crosses a mount at the supplied root.</summary>
    ValueTask<NamespaceChannel> AttachAsync(ResourceHandle root, CancellationToken cancellationToken);

    /// <summary>Walks from an existing channel.</summary>
    ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken);

    /// <summary>Opens the resource selected by a channel.</summary>
    ValueTask<ResourceOpenHandle> OpenAsync(
        NamespaceChannel channel,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads an open resource.</summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata for the concatenated visible directory.</summary>
    ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken);

    /// <summary>Writes an open resource.</summary>
    ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata through the namespace.</summary>
    ValueTask<ResourceStat> StatAsync(NamespaceChannel channel, CancellationToken cancellationToken);

    /// <summary>Reads metadata for an already opened resource.</summary>
    ValueTask<ResourceStat> StatAsync(ResourceOpenHandle openHandle, CancellationToken cancellationToken);

    /// <summary>Creates in the selected union member and opens the child.</summary>
    ValueTask<NamespaceCreateResult> CreateAndOpenAsync(
        NamespaceChannel channel,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Closes provider-owned open state.</summary>
    ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Removes the selected resource.</summary>
    ValueTask RemoveAsync(
        NamespaceChannel channel,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

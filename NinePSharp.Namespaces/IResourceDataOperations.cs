using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Extends namespace traversal with stateful file operations.</summary>
public interface IResourceDataOperations : IResourceOperations
{
    /// <summary>Opens a resource and returns provider-owned open state.</summary>
    ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads bytes from an open resource.</summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken);

    /// <summary>Writes bytes to an open resource.</summary>
    ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata for a resource.</summary>
    ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken);

    /// <summary>
    /// Creates and opens a previously absent child atomically (9P create(5)). An
    /// existing name must fail without truncation. Replay must return the original
    /// handle. Use ResourceCreateRejectedException only for a definite rejection;
    /// ordinary IO/transport failures must never imply that creation did not commit.
    /// Native create(2) lookup/truncate behavior belongs to Plan9FileSyscalls.
    /// </summary>
    ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
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

    /// <summary>Removes a resource. The caller invalidates its fid regardless of the result.</summary>
    ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

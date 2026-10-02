using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Raw bounded stat operations. Implementations own any temporary provider fids through completion and cleanup.</summary>
public interface IFileStatOperations
{
    /// <summary>Stats a resolved resource without opening it for content IO. Returns one record or its two-byte size hint.</summary>
    ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken);

    /// <summary>Stats the retained provider open instance, without looking its name up again or consuming it.</summary>
    ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken);

    /// <summary>Applies one validated raw update to a resolved resource.</summary>
    ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Applies one validated raw update through a retained open instance.</summary>
    ValueTask<uint> WStatAsync(
        ResourceOpenHandle handle,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

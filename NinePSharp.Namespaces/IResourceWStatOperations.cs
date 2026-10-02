using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Optional provider capability for atomic metadata mutation.</summary>
public interface IResourceWStatOperations
{
    /// <summary>Applies one update atomically to a resolved resource.</summary>
    ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Applies one update atomically through a retained open instance.</summary>
    ValueTask<uint> WStatOpenAsync(
        ResourceOpenHandle handle,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

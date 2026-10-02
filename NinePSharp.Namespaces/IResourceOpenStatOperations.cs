using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Optional provider capability for metadata on a retained open instance.</summary>
public interface IResourceOpenStatOperations
{
    /// <summary>Returns current metadata using the supplied open handle, including any provider-specific post-removal policy.</summary>
    ValueTask<ResourceStat> StatOpenAsync(ResourceOpenHandle handle, CancellationToken cancellationToken);
}

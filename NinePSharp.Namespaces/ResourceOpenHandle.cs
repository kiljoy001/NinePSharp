using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Provider-owned state associated with an open fid.</summary>
public sealed record ResourceOpenHandle
{
    public ResourceOpenHandle(ResourceHandle resource, string handleId, byte mode, uint ioUnit, bool isMountTransport = false)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(handleId);
        HandleId = handleId;
        Mode = mode;
        IoUnit = ioUnit;
        IsMountTransport = isMountTransport;
    }

    /// <summary>Gets the opened resource.</summary>
    public ResourceHandle Resource { get; }

    /// <summary>Gets the provider-owned open-handle identity.</summary>
    public string HandleId { get; }

    /// <summary>Gets the Plan 9 open mode.</summary>
    public byte Mode { get; }

    /// <summary>Gets the maximum provider-specific I/O payload, or zero for the connection default.</summary>
    public uint IoUnit { get; }

    /// <summary>Gets a value indicating whether this is the native mount protocol transport channel (CMSG).</summary>
    public bool IsMountTransport { get; }
}

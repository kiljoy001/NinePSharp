using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>An immutable path or retained-handle mutation saved before provider dispatch.</summary>
public sealed class WStatRecoveryRequest
{
    private readonly byte[] stat;

    private WStatRecoveryRequest(
        ResourceOperationContext context,
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ReadOnlyMemory<byte> stat)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        OpenHandle = openHandle;
        this.stat = stat.ToArray();
        Fingerprint = FingerprintOf(context, resource, openHandle, this.stat);
    }

    /// <summary>Gets the original authenticated operation context.</summary>
    public ResourceOperationContext Context { get; }

    /// <summary>Gets the stable resource selected before dispatch.</summary>
    public ResourceHandle Resource { get; }

    /// <summary>Gets the retained provider handle for fwstat, or null for path wstat.</summary>
    public ResourceOpenHandle? OpenHandle { get; }

    /// <summary>Gets an immutable view of the exact validated raw request.</summary>
    public ReadOnlyMemory<byte> Stat => stat;

    /// <summary>Gets the canonical request fingerprint used to reject operation-ID reuse.</summary>
    public string Fingerprint { get; }

    /// <summary>Creates a saved path-wstat request after namespace selection.</summary>
    public static WStatRecoveryRequest ForResource(
        ResourceHandle resource,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context)
        => new(context, resource, null, stat);

    /// <summary>Creates a saved fwstat request using its retained provider handle.</summary>
    public static WStatRecoveryRequest ForOpenHandle(
        ResourceOpenHandle handle,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new(context, handle.Resource, handle, stat);
    }

    private static string FingerprintOf(
        ResourceOperationContext context,
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        byte[] stat)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8);
        writer.Write(context.ProcessId);
        writer.Write(context.User);
        writer.Write(resource.Identity.Provider);
        writer.Write(resource.Identity.Device);
        writer.Write(resource.Identity.Path);
        writer.Write((byte)resource.Type);
        writer.Write(resource.Version);
        writer.Write(openHandle is not null);
        if (openHandle is not null)
        {
            writer.Write(openHandle.HandleId);
            writer.Write(openHandle.Mode);
            writer.Write(openHandle.IoUnit);
            writer.Write(openHandle.IsMountTransport);
        }

        writer.Write(stat.Length);
        writer.Write(stat);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
    }
}

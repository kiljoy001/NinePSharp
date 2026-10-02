using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Adapts typed resource metadata to bounded native stat records with explicit device mappings.</summary>
public sealed class FileStatOperations : IFileStatOperations
{
    private readonly IResourceDataOperations resources;
    private readonly DirectoryStatOperations codec;

    /// <summary>Initializes a new instance of the <see cref="FileStatOperations"/> class. Fstat additionally requires IResourceOpenStatOperations on the provider.</summary>
    public FileStatOperations(IResourceDataOperations resources, IEnumerable<DirectoryDeviceBinding> devices)
    {
        codec = new DirectoryStatOperations(resources, devices);
        this.resources = resources;
    }

    /// <summary>Encodes one typed update as an exact native little-endian stat record.</summary>
    public static byte[] EncodeUpdate(ResourceWStat stat) => FileStatRecords.EncodeUpdate(stat);

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
    {
        ValidateCount(count);
        return Bound(codec.Encode(await resources.StatAsync(resource, cancellationToken)), count);
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken)
    {
        ValidateCount(count);
        var opened = resources as IResourceOpenStatOperations
            ?? throw new NotSupportedException("The provider does not support metadata through open handles.");
        return Bound(codec.Encode(await opened.StatOpenAsync(handle, cancellationToken)), count);
    }

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        var updates = resources as IResourceWStatOperations
            ?? throw new NotSupportedException("The provider does not support metadata mutation.");
        return updates.WStatAsync(resource, FileStatRecords.DecodeUpdate(stat), context, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(
        ResourceOpenHandle handle,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        var updates = resources as IResourceWStatOperations
            ?? throw new NotSupportedException("The provider does not support metadata mutation through open handles.");
        return updates.WStatOpenAsync(handle, FileStatRecords.DecodeUpdate(stat), context, cancellationToken);
    }

    internal static void ValidateCount(uint count)
    {
        if (count < 2)
        {
            throw new NamespaceFidException("short stat buffer");
        }
    }

    private static ReadOnlyMemory<byte> Bound(byte[] bytes, uint count)
        => bytes.Length > count ? bytes.AsMemory(0, 2) : bytes;
}

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
    ValueTask<uint> WStatAsync(ResourceHandle resource, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken);

    /// <summary>Applies one validated raw update through a retained open instance.</summary>
    ValueTask<uint> WStatAsync(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken);
}

/// <summary>Optional provider capability for metadata on a retained open instance.</summary>
public interface IResourceOpenStatOperations
{
    /// <summary>Returns current metadata using the supplied open handle, including any provider-specific post-removal policy.</summary>
    ValueTask<ResourceStat> StatOpenAsync(ResourceOpenHandle handle, CancellationToken cancellationToken);
}

/// <summary>Optional provider capability for atomic metadata mutation.</summary>
public interface IResourceWStatOperations
{
    /// <summary>Applies one update atomically to a resolved resource.</summary>
    ValueTask<uint> WStatAsync(ResourceHandle resource, ResourceWStat stat,
        ResourceOperationContext context, CancellationToken cancellationToken);

    /// <summary>Applies one update atomically through a retained open instance.</summary>
    ValueTask<uint> WStatOpenAsync(ResourceOpenHandle handle, ResourceWStat stat,
        ResourceOperationContext context, CancellationToken cancellationToken);
}

/// <summary>Adapts typed resource metadata to bounded native stat records with explicit device mappings.</summary>
public sealed class FileStatOperations : IFileStatOperations
{
    private readonly IResourceDataOperations resources;
    private readonly DirectoryStatOperations codec;

    /// <summary>Creates an adapter. Fstat additionally requires IResourceOpenStatOperations on the provider.</summary>
    public FileStatOperations(IResourceDataOperations resources, IEnumerable<DirectoryDeviceBinding> devices)
    {
        codec = new DirectoryStatOperations(resources, devices);
        this.resources = resources;
    }

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
    public ValueTask<uint> WStatAsync(ResourceHandle resource, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
    {
        var updates = resources as IResourceWStatOperations
            ?? throw new NotSupportedException("The provider does not support metadata mutation.");
        return updates.WStatAsync(resource, FileStatRecords.DecodeUpdate(stat), context, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
    {
        var updates = resources as IResourceWStatOperations
            ?? throw new NotSupportedException("The provider does not support metadata mutation through open handles.");
        return updates.WStatOpenAsync(handle, FileStatRecords.DecodeUpdate(stat), context, cancellationToken);
    }

    /// <summary>Encodes one typed update as an exact native little-endian stat record.</summary>
    public static byte[] EncodeUpdate(ResourceWStat stat) => FileStatRecords.EncodeUpdate(stat);

    private static ReadOnlyMemory<byte> Bound(byte[] bytes, uint count)
        => bytes.Length > count ? bytes.AsMemory(0, 2) : bytes;

    internal static void ValidateCount(uint count)
    {
        if (count < 2) throw new NamespaceFidException("short stat buffer");
    }
}

internal static class FileStatRecords
{
    private static readonly Encoding StrictUtf8 = Encoding.GetEncoding(
        Encoding.UTF8.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    internal static byte[] ValidateAndCopyUpdate(ReadOnlyMemory<byte> stat)
    {
        ReadOnlySpan<byte> bytes = stat.Span;
        if (bytes.Length < 49) throw new NamespaceFidException("bad stat");
        try
        {
            DirectoryRecords.Validate(bytes, (uint)bytes.Length);
        }
        catch (Exception error) when (error is IOException or OverflowException)
        {
            throw new NamespaceFidException("bad stat");
        }

        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[41..]);
        if (nameLength < 64)
        {
            ReadOnlySpan<byte> name = bytes.Slice(43, nameLength);
            int terminator = name.IndexOf((byte)0);
            if (terminator >= 0) name = name[..terminator];
            bool slash = name.Length == 1 && name[0] == (byte)'/';
            if (!slash && (name.IndexOfAnyInRange((byte)1, (byte)31) >= 0
                || name.IndexOf((byte)'/') >= 0 || name.IndexOf((byte)127) >= 0))
                throw new NamespaceFidException("bad character in file name");
        }

        return bytes.ToArray();
    }

    internal static ResourceWStat DecodeUpdate(ReadOnlyMemory<byte> stat)
    {
        ReadOnlySpan<byte> bytes = stat.Span;
        int offset = 2;
        ushort type = ReadUInt16(bytes, ref offset);
        uint device = ReadUInt32(bytes, ref offset);
        QidType qidType = (QidType)bytes[offset++];
        uint qidVersion = ReadUInt32(bytes, ref offset);
        ulong qidPath = ReadUInt64(bytes, ref offset);
        uint mode = ReadUInt32(bytes, ref offset);
        uint accessTime = ReadUInt32(bytes, ref offset);
        uint modificationTime = ReadUInt32(bytes, ref offset);
        ulong length = ReadUInt64(bytes, ref offset);
        string name = ReadString(bytes, ref offset);
        string user = ReadString(bytes, ref offset);
        string group = ReadString(bytes, ref offset);
        string lastModifier = ReadString(bytes, ref offset);
        return new ResourceWStat(type, device, new Qid(qidType, qidVersion, qidPath), mode,
            accessTime, modificationTime, length, name, user, group, lastModifier, (uint)bytes.Length);
    }

    internal static byte[] EncodeUpdate(ResourceWStat stat)
    {
        ArgumentNullException.ThrowIfNull(stat);
        byte[][] strings = new[] { stat.Name, stat.User, stat.Group, stat.LastModifier }
            .Select(StrictUtf8.GetBytes).ToArray();
        int size = 49 + strings.Sum(value => value.Length);
        if (size - 2 > ushort.MaxValue) throw new IOException("wstat exceeds the wire record limit");
        byte[] result = new byte[size];
        int offset = 0;
        WriteUInt16(result, ref offset, (ushort)(size - 2));
        WriteUInt16(result, ref offset, stat.Type);
        WriteUInt32(result, ref offset, stat.Device);
        result[offset++] = (byte)stat.Qid.Type;
        WriteUInt32(result, ref offset, stat.Qid.Version);
        WriteUInt64(result, ref offset, stat.Qid.Path);
        WriteUInt32(result, ref offset, stat.Mode);
        WriteUInt32(result, ref offset, stat.AccessTime);
        WriteUInt32(result, ref offset, stat.ModificationTime);
        WriteUInt64(result, ref offset, stat.Length);
        foreach (byte[] value in strings)
        {
            WriteUInt16(result, ref offset, (ushort)value.Length);
            value.CopyTo(result, offset);
            offset += value.Length;
        }
        return result;
    }

    internal static ReadOnlyMemory<byte> Rewrite(ReadOnlyMemory<byte> reply, uint count, string? name)
    {
        if (reply.Length < 2 || reply.Length > count)
            throw new IOException("invalid stat reply length");
        int size = BinaryPrimitives.ReadUInt16LittleEndian(reply.Span) + 2;
        if (size < 49) throw new IOException("invalid stat size");
        // sysfile.c:dirsetname cannot rewrite a provider's size-only response.
        if (reply.Length == 2) return reply.ToArray();
        if (size != reply.Length) throw new IOException("stat must contain exactly one complete record");
        DirectoryRecords.Validate(reply.Span, count);
        if (name is null) return reply.ToArray();

        byte[] encoded = Encoding.UTF8.GetBytes(name);
        int previous = BinaryPrimitives.ReadUInt16LittleEndian(reply.Span[41..]);
        int length = size - previous;
        if (encoded.Length > ushort.MaxValue + 2 - length) throw new IOException("visible stat name exceeds the wire record limit");
        length += encoded.Length;
        byte[] result = new byte[length > count ? 2 : length];
        BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)(length - 2));
        if (result.Length == 2) return result;
        reply.Span[2..41].CopyTo(result.AsSpan(2));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(41), (ushort)encoded.Length);
        encoded.CopyTo(result.AsSpan(43));
        reply.Span[(43 + previous)..].CopyTo(result.AsSpan(43 + encoded.Length));
        return result;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
        offset += 8;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        int length = ReadUInt16(bytes, ref offset);
        string value;
        try { value = StrictUtf8.GetString(bytes.Slice(offset, length)); }
        catch (DecoderFallbackException) { throw new NamespaceFidException("bad UTF-8 in stat"); }
        offset += length;
        return value;
    }

    private static void WriteUInt16(Span<byte> bytes, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);
        offset += 2;
    }

    private static void WriteUInt32(Span<byte> bytes, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
        offset += 4;
    }

    private static void WriteUInt64(Span<byte> bytes, ref int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[offset..], value);
        offset += 8;
    }
}

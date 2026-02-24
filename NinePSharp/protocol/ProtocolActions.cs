using System.Text;
using System.Buffers.Binary;
using NinePSharp.Constants;

namespace NinePSharp.Protocol;

/// <summary>
/// Provides extension methods for serializing and deserializing 9P protocol primitives.
/// </summary>
public static class ProtocolActions
{
    /// <summary>
    /// Writes a UTF-8 string to the byte span with a 2-byte length prefix.
    /// </summary>
    /// <param name="data">The target byte span.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="byteIndex">The current index in the span, updated after writing.</param>
    public static void WriteString(this Span<byte> data, string value, ref int byteIndex )
    {
        var len = (ushort)Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(byteIndex, 2), len);
        byteIndex += 2;
        
        Encoding.UTF8.GetBytes(value, data.Slice(byteIndex));
        byteIndex += len;
    }

    /// <summary>
    /// Reads a UTF-8 string from the byte span using its 2-byte length prefix.
    /// </summary>
    /// <param name="data">The source byte span.</param>
    /// <param name="byteIndex">The current index in the span, updated after reading.</param>
    /// <returns>The string read from the span.</returns>
    public static string ReadString(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        if (byteIndex + 2 > data.Length) throw new IndexOutOfRangeException();
        var len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(byteIndex, 2));
        byteIndex += 2;
        
        var value = Encoding.UTF8.GetString(data.Slice(byteIndex, len));
        byteIndex += len;
        return value;
    }

    /// <summary>
    /// Writes the standard 9P message header (size, type, and tag).
    /// </summary>
    /// <param name="data">The target byte span.</param>
    /// <param name="size">The total message size.</param>
    /// <param name="tag">The message tag.</param>
    /// <param name="type">The message type.</param>
    public static void WriteHeaders(this Span<byte> data, uint size, ushort tag, MessageTypes type)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data[..4], size);
        data[4] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(5, 2), tag);
    }

    /// <summary>
    /// Reads a 13-byte QID identifier from the byte span.
    /// </summary>
    /// <param name="data">The source byte span.</param>
    /// <param name="byteIndex">The current index in the span, updated after reading.</param>
    /// <returns>The QID struct.</returns>
    public static Qid ReadQid(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        // Qid is 13 bytes - Type[1] Version[4] Path[8]
        var type = (QidType)data[byteIndex];
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(byteIndex + 1, 4));
        var path = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(byteIndex + 5, 8));
        byteIndex += 13;
        return new Qid(type, version, path);
    }

    /// <summary>
    /// Writes a 13-byte QID identifier to the byte span.
    /// </summary>
    /// <param name="data">The target byte span.</param>
    /// <param name="qid">The QID to write.</param>
    /// <param name="byteIndex">The current index in the span, updated after writing.</param>
    public static void WriteQid(this Span<byte> data, Qid qid, ref int byteIndex)
    {
        data[byteIndex] = (byte)qid.Type;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(byteIndex + 1, 4), qid.Version);
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(byteIndex + 5, 8), qid.Path);
        byteIndex += 13;
    }
}
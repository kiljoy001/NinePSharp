using System.Buffers.Binary;
using NinePSharp.Constants;
using Plan9.Utf;

namespace NinePSharp.Protocol;

/// <summary>
/// Provides extension methods for serializing and deserializing 9P protocol primitives.
/// </summary>
public static class ProtocolActions
{
    private const int StringLengthPrefixSize = 2;
    private const int HeaderSize = 7;
    private const int QidSize = 13;

    /// <summary>
    /// Gets the bytes a string takes in a 9P message, its 2-byte length included: Plan 9's UTF-8,
    /// in which an unpaired surrogate is written as the rune it is.
    /// </summary>
    /// <param name="value">The string, where null is written as the empty string.</param>
    /// <returns>The string's size on the wire.</returns>
    public static int StringSize(string? value) => StringLengthPrefixSize + Plan9Utf8Encoding.Instance.GetByteCount(value ?? string.Empty);

    /// <summary>
    /// Writes a string to the byte span as Plan 9's UTF-8 with a 2-byte length prefix.
    /// </summary>
    /// <param name="data">The target byte span.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="byteIndex">The current index in the span, updated after writing.</param>
    public static void WriteString(this Span<byte> data, string value, ref int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(value);

        var length = Plan9Utf8Encoding.Instance.GetByteCount(value);
        if (length > ushort.MaxValue)
        {
            throw new InvalidOperationException("9P string exceeds the 65535-byte length limit.");
        }

        EnsureWritable(data, byteIndex, StringLengthPrefixSize + length, "the string");

        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(byteIndex, StringLengthPrefixSize), (ushort)length);
        var valueIndex = byteIndex + StringLengthPrefixSize;
        Plan9Utf8Encoding.Instance.GetBytes(value, data.Slice(valueIndex, length));

        byteIndex += StringLengthPrefixSize + length;
    }

    /// <summary>
    /// Reads a string from the byte span using its 2-byte length prefix, as Plan 9 reads UTF-8: each
    /// bad byte is a Runeerror.
    /// </summary>
    /// <param name="data">The source byte span.</param>
    /// <param name="byteIndex">The current index in the span, updated after reading.</param>
    /// <returns>The string read from the span.</returns>
    public static string ReadString(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        EnsureReadable(data, byteIndex, StringLengthPrefixSize, "the string length");

        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(byteIndex, StringLengthPrefixSize));
        var valueIndex = byteIndex + StringLengthPrefixSize;
        EnsureReadable(data, valueIndex, length, "the string payload");

        var value = Plan9Utf8Encoding.Instance.GetString(data.Slice(valueIndex, length));
        byteIndex += StringLengthPrefixSize + length;
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
        EnsureWritable(data, 0, HeaderSize, "the 9P header");

        BinaryPrimitives.WriteUInt32LittleEndian(data[..4], size);
        data[4] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(5, StringLengthPrefixSize), tag);
    }

    /// <summary>
    /// Reads a 13-byte QID identifier from the byte span.
    /// </summary>
    /// <param name="data">The source byte span.</param>
    /// <param name="byteIndex">The current index in the span, updated after reading.</param>
    /// <returns>The QID struct.</returns>
    public static Qid ReadQid(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        EnsureReadable(data, byteIndex, QidSize, "the QID");

        var type = (QidType)data[byteIndex];
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(byteIndex + 1, 4));
        var path = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(byteIndex + 5, 8));
        byteIndex += QidSize;
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
        EnsureWritable(data, byteIndex, QidSize, "the QID");

        data[byteIndex] = (byte)qid.Type;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(byteIndex + 1, 4), qid.Version);
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(byteIndex + 5, 8), qid.Path);
        byteIndex += QidSize;
    }

    private static void EnsureReadable(ReadOnlySpan<byte> data, int byteIndex, int byteCount, string valueName)
    {
        if (byteIndex < 0)
        {
            throw new InvalidOperationException($"Cannot read {valueName} at a negative offset.");
        }

        if (byteCount > data.Length || byteIndex > data.Length - byteCount)
        {
            throw new InvalidOperationException($"Insufficient bytes to read {valueName}.");
        }
    }

    private static void EnsureWritable(Span<byte> data, int byteIndex, int byteCount, string valueName)
    {
        if (byteIndex < 0)
        {
            throw new InvalidOperationException($"Cannot write {valueName} at a negative offset.");
        }

        if (byteCount > data.Length || byteIndex > data.Length - byteCount)
        {
            throw new InvalidOperationException($"Insufficient bytes to write {valueName}.");
        }
    }
}

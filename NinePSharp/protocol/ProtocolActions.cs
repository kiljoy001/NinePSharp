using System.Text;
using System.Buffers.Binary;
using NinePSharp.Constants;

namespace NinePSharp.Protocol;

public static class ProtocolActions
{
    public static void WriteString(this Span<byte> data, string value, ref int byteIndex )
    {
        var len = (ushort)Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(byteIndex, 2), len);
        byteIndex += 2;
        
        Encoding.UTF8.GetBytes(value, data.Slice(byteIndex));
        byteIndex += len;
    }

    public static string ReadString(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        if (byteIndex + 2 > data.Length) throw new IndexOutOfRangeException();
        var len = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(byteIndex, 2));
        byteIndex += 2;
        
        var value = Encoding.UTF8.GetString(data.Slice(byteIndex, len));
        byteIndex += len;
        return value;
    }

    public static void WriteHeaders(this Span<byte> data, uint size, ushort tag, MessageTypes type)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data[..4], size);
        data[4] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(5, 2), tag);
    }

    public static Qid ReadQid(this ReadOnlySpan<byte> data, ref int byteIndex)
    {
        // Qid is 13 bytes - Type[1] Version[4] Path[8]
        var type = (QidType)data[byteIndex];
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(byteIndex + 1, 4));
        var path = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(byteIndex + 5, 8));
        byteIndex += 13;
        return new Qid(type, version, path);
    }

    public static void WriteQid(this Span<byte> data, Qid qid, ref int byteIndex)
    {
        data[byteIndex] = (byte)qid.Type;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(byteIndex + 1, 4), qid.Version);
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(byteIndex + 5, 8), qid.Path);
        byteIndex += 13;
    }
}
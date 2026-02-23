namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Treaddir : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Treaddir;
    public ushort Tag { get; }
    public uint Fid { get; }
    public ulong Offset { get; }
    public uint Count { get; }

    public Treaddir(uint size, ushort tag, uint fid, ulong offset, uint count)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Offset = offset;
        Count = count;
    }

    public Treaddir(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Offset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Treaddir);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), Offset); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Count);
    }
}

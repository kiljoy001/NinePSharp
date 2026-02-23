namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Txattrcreate : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Txattrcreate;
    public ushort Tag { get; }
    public uint Fid { get; }
    public string Name { get; }
    public ulong AttrSize { get; }
    public uint Flags { get; }

    public Txattrcreate(uint size, ushort tag, uint fid, string name, ulong attrSize, uint flags)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Name = name;
        AttrSize = attrSize;
        Flags = flags;
    }

    public Txattrcreate(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
        AttrSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Txattrcreate);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        span.WriteString(Name, ref offset);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), AttrSize); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Flags);
    }
}

namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tlcreate : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tlcreate;
    public ushort Tag { get; }
    public uint Fid { get; }
    public string Name { get; }
    public uint Flags { get; }
    public uint Mode { get; }
    public uint Gid { get; }

    public Tlcreate(uint size, ushort tag, uint fid, string name, uint flags, uint mode, uint gid)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Name = name;
        Flags = flags;
        Mode = mode;
        Gid = gid;
    }

    public Tlcreate(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tlcreate);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        span.WriteString(Name, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Flags); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Mode); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Gid);
    }
}

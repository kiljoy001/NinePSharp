namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tsymlink : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tsymlink;
    public ushort Tag { get; }
    public uint Fid { get; }
    public string Name { get; }
    public string Symtgt { get; }
    public uint Gid { get; }

    public Tsymlink(uint size, ushort tag, uint fid, string name, string symtgt, uint gid)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Name = name;
        Symtgt = symtgt;
        Gid = gid;
    }

    public Tsymlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
        Symtgt = data.ReadString(ref offset);
        Gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tsymlink);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        span.WriteString(Name, ref offset);
        span.WriteString(Symtgt, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Gid);
    }
}

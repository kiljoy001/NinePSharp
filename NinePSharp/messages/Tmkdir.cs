namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tmkdir : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tmkdir;
    public ushort Tag { get; }
    public uint Dfid { get; }
    public string Name { get; }
    public uint Mode { get; }
    public uint Gid { get; }

    public Tmkdir(uint size, ushort tag, uint dfid, string name, uint mode, uint gid)
    {
        Size = size;
        Tag = tag;
        Dfid = dfid;
        Name = name;
        Mode = mode;
        Gid = gid;
    }

    public Tmkdir(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Dfid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
        Mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tmkdir);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Dfid); offset += 4;
        span.WriteString(Name, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Mode); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Gid);
    }
}

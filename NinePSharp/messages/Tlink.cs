namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tlink : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tlink;
    public ushort Tag { get; }
    public uint Dfid { get; }
    public uint Fid { get; }
    public string Name { get; }

    public Tlink(uint size, ushort tag, uint dfid, uint fid, string name)
    {
        Size = size;
        Tag = tag;
        Dfid = dfid;
        Fid = fid;
        Name = name;
    }

    public Tlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Dfid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tlink);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Dfid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        span.WriteString(Name, ref offset);
    }
}

namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Trename : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Trename;
    public ushort Tag { get; }
    public uint Fid { get; }
    public uint Dfid { get; }
    public string Name { get; }

    public Trename(uint size, ushort tag, uint fid, uint dfid, string name)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Dfid = dfid;
        Name = name;
    }

    public Trename(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Dfid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Trename);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Dfid); offset += 4;
        span.WriteString(Name, ref offset);
    }
}

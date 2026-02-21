namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Txattrwalk : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Txattrwalk;
    public ushort Tag { get; }
    public uint Fid { get; }
    public uint NewFid { get; }
    public string Name { get; }

    public Txattrwalk(uint size, ushort tag, uint fid, uint newFid, string name)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        NewFid = newFid;
        Name = name;
    }

    public Txattrwalk(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        NewFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Txattrwalk);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), NewFid); offset += 4;
        span.WriteString(Name, ref offset);
    }
}

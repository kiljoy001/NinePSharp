namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rlopen : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.RLopen;
    public ushort Tag { get; }
    public Qid Qid { get; }
    public uint Iounit { get; }

    public Rlopen(uint size, ushort tag, Qid qid, uint iounit)
    {
        Size = size;
        Tag = tag;
        Qid = qid;
        Iounit = iounit;
    }

    public Rlopen(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Qid = data.ReadQid(ref offset);
        Iounit = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.RLopen);
        int offset = NinePConstants.HeaderSize;
        span.WriteQid(Qid, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Iounit);
    }
}

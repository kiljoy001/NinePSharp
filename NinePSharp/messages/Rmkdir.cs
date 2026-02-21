namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rmkdir : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rmkdir;
    public ushort Tag { get; }
    public Qid Qid { get; }

    public Rmkdir(uint size, ushort tag, Qid qid)
    {
        Size = size;
        Tag = tag;
        Qid = qid;
    }

    public Rmkdir(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Qid = data.ReadQid(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rmkdir);
        int offset = NinePConstants.HeaderSize;
        span.WriteQid(Qid, ref offset);
    }
}

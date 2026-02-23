namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rsymlink : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rsymlink;
    public ushort Tag { get; }
    public Qid Qid { get; }

    public Rsymlink(uint size, ushort tag, Qid qid)
    {
        Size = size;
        Tag = tag;
        Qid = qid;
    }

    public Rsymlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Qid = data.ReadQid(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rsymlink);
        int offset = NinePConstants.HeaderSize;
        span.WriteQid(Qid, ref offset);
    }
}

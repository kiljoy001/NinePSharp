using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rattach tag[2] qid[13]
public readonly struct Rattach : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rattach;
    public ushort Tag { get; }

    public Qid Qid { get; }

    public Rattach(ushort tag, Qid qid)
    {
        Tag = tag;
        Qid = qid;
        Size = NinePConstants.HeaderSize + 13;
    }

    public Rattach(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Qid = data.ReadQid(ref offset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
        data.WriteQid(Qid, ref offset);
    }
}

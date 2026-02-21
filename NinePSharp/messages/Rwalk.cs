using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rwalk tag[2] nwqid[2] nwqid*(wqid[13])
public readonly struct Rwalk : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rwalk;
    public ushort Tag { get; }

    public Qid[] Wqid { get; }

    public Rwalk(ushort tag, Qid[] wqid)
    {
        Tag = tag;
        Wqid = wqid ?? Array.Empty<Qid>();
        Size = (uint)(NinePConstants.HeaderSize + 2 + (Wqid.Length * 13));
    }

    public Rwalk(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        ushort nwqid = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        Wqid = new Qid[nwqid];
        for (int i = 0; i < nwqid; i++)
        {
            Wqid[i] = data.ReadQid(ref offset);
        }
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        ushort nwqid = (ushort)(Wqid?.Length ?? 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), nwqid);
        offset += 2;

        for (int i = 0; i < nwqid; i++)
        {
            data.WriteQid(Wqid![i], ref offset);
        }
    }
}

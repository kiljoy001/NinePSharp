using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Twstat tag[2] fid[4] nstat[2] stat[nstat]
public readonly struct Twstat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Twstat;
    public ushort Tag { get; }

    public uint Fid { get; }
    public ushort NStat { get; }
    public Stat Stat { get; }

    public Twstat(ushort tag, uint fid, Stat stat)
    {
        Tag = tag;
        Fid = fid;
        Stat = stat;
        NStat = Stat.Size;
        // Standard 9P2000 framing: Header(7) + fid[4] + nstat[2] + stat[nstat]
        Size = (uint)(NinePConstants.HeaderSize + 4 + 2 + NStat);
    }

    public Twstat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        NStat = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        int statOffset = 0;
        Stat = new Stat(data.Slice(offset, NStat), ref statOffset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), NStat);
        offset += 2;

        Stat.WriteTo(data, ref offset);
    }
}

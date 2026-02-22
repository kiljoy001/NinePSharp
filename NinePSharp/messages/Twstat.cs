using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Twstat tag[2] fid[4] stat[n]
public readonly struct Twstat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Twstat;
    public ushort Tag { get; }

    public uint Fid { get; }
    public Stat Stat { get; }

    public Twstat(ushort tag, uint fid, Stat stat)
    {
        Tag = tag;
        Fid = fid;
        Stat = stat;
        // Standard 9P2000 framing: Header(7) + fid[4] + stat[n]
        Size = (uint)(NinePConstants.HeaderSize + 4 + Stat.Size);
    }

    public Twstat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Stat = new Stat(data, ref offset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        Stat.WriteTo(data, ref offset);
    }
}

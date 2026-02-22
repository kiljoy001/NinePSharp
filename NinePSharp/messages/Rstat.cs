using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rstat tag[2] nstat[2] stat[nstat]
public readonly struct Rstat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rstat;
    public ushort Tag { get; }
    public ushort NStat { get; }

    public Stat Stat { get; }

    public Rstat(ushort tag, Stat stat)
    {
        Tag = tag;
        Stat = stat;
        NStat = Stat.Size;
        // Standard 9P2000 framing: Header(7) + nstat[2] + stat[nstat]
        Size = (uint)(NinePConstants.HeaderSize + 2 + NStat);
    }

    public Rstat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        NStat = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        int statOffset = 0;
        Stat = new Stat(data.Slice(offset, NStat), ref statOffset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), NStat);
        offset += 2;

        Stat.WriteTo(data, ref offset);
    }
}

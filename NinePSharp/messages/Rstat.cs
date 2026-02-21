using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rstat tag[2] stat[n]
public readonly struct Rstat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rstat;
    public ushort Tag { get; }

    public Stat Stat { get; }

    public Rstat(ushort tag, Stat stat)
    {
        Tag = tag;
        Stat = stat;
        Size = (uint)(NinePConstants.HeaderSize + Stat.Size);
    }

    public Rstat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Stat = new Stat(data, ref offset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        Stat.WriteTo(data, ref offset);
    }
}

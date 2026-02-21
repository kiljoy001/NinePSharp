using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rwstat tag[2]
public readonly struct Rwstat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rwstat;
    public ushort Tag { get; }

    public Rwstat(ushort tag)
    {
        Tag = tag;
        Size = NinePConstants.HeaderSize;
    }

    public Rwstat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
    }
}

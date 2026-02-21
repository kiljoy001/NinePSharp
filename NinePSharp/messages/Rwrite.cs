using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rwrite tag[2] count[4]
public readonly struct Rwrite : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rwrite;
    public ushort Tag { get; }

    public uint Count { get; }

    public Rwrite(ushort tag, uint count)
    {
        Tag = tag;
        Count = count;
        Size = NinePConstants.HeaderSize + 4;
    }

    public Rwrite(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Count);
    }
}

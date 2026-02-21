using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Tflush tag[2] oldtag[2]
public readonly struct Tflush : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tflush;
    public ushort Tag { get; }

    public ushort OldTag { get; }

    public Tflush(ushort tag, ushort oldtag)
    {
        Tag = tag;
        OldTag = oldtag;
        Size = NinePConstants.HeaderSize + 2;
    }

    public Tflush(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        OldTag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), OldTag);
    }
}

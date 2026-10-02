using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Rsetattr : ISerializable
{
    public Rsetattr(uint size, ushort tag)
    {
        Size = size;
        Tag = tag;
    }

    public Rsetattr(ushort tag)
    {
        Size = NinePConstants.HeaderSize;
        Tag = tag;
    }

    public Rsetattr(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public uint Size { get; }

    public MessageTypes Type => MessageTypes.Rsetattr;

    public ushort Tag { get; }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rsetattr);
    }
}

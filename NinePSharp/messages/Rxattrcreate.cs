using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Rxattrcreate : ISerializable
{
    public Rxattrcreate(uint size, ushort tag)
    {
        Size = size;
        Tag = tag;
    }

    public Rxattrcreate(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public uint Size { get; }

    public MessageTypes Type => MessageTypes.Rxattrcreate;

    public ushort Tag { get; }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rxattrcreate);
    }
}

namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rlink : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rlink;
    public ushort Tag { get; }

    public Rlink(uint size, ushort tag)
    {
        Size = size;
        Tag = tag;
    }

    public Rlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rlink);
    }
}

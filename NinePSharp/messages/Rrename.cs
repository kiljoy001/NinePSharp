namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rrename : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rrename;
    public ushort Tag { get; }

    public Rrename(uint size, ushort tag)
    {
        Size = size;
        Tag = tag;
    }

    public Rrename(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rrename);
    }
}

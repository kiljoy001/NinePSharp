namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rlock : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rlock;
    public ushort Tag { get; }
    public byte Status { get; }

    public Rlock(uint size, ushort tag, byte status)
    {
        Size = size;
        Tag = tag;
        Status = status;
    }

    public Rlock(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        Status = data[NinePConstants.HeaderSize];
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rlock);
        span[NinePConstants.HeaderSize] = Status;
    }
}

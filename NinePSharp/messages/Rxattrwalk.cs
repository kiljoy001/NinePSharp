namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rxattrwalk : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rxattrwalk;
    public ushort Tag { get; }
    public ulong XattrSize { get; }

    public Rxattrwalk(uint size, ushort tag, ulong xattrSize)
    {
        Size = size;
        Tag = tag;
        XattrSize = xattrSize;
    }

    public Rxattrwalk(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        XattrSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rxattrwalk);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), XattrSize);
    }
}

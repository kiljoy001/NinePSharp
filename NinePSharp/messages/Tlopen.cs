namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tlopen : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tlopen;
    public ushort Tag { get; }
    public uint Fid { get; }
    public uint Flags { get; }

    public Tlopen(uint size, ushort tag, uint fid, uint flags)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Flags = flags;
    }

    public Tlopen(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(NinePConstants.HeaderSize, 4));
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(NinePConstants.HeaderSize + 4, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tlopen);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Flags);
    }
}

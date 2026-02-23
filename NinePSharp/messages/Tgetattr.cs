namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tgetattr : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tgetattr;
    public ushort Tag { get; }
    public uint Fid { get; }
    public ulong RequestMask { get; }

    public Tgetattr(uint size, ushort tag, uint fid, ulong requestMask)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        RequestMask = requestMask;
    }

    public Tgetattr(ushort tag, uint fid, ulong requestMask)
    {
        Size = NinePConstants.HeaderSize + 4 + 8;
        Tag = tag;
        Fid = fid;
        RequestMask = requestMask;
    }

    public Tgetattr(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        RequestMask = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tgetattr);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), RequestMask);
    }
}

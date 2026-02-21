namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Treadlink : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Treadlink;
    public ushort Tag { get; }
    public uint Fid { get; }

    public Treadlink(uint size, ushort tag, uint fid)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
    }

    public Treadlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(NinePConstants.HeaderSize, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Treadlink);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(NinePConstants.HeaderSize, 4), Fid);
    }
}

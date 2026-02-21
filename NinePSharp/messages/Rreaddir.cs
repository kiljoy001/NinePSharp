namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rreaddir : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rreaddir;
    public ushort Tag { get; }
    public uint Count { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public Rreaddir(uint size, ushort tag, uint count, ReadOnlyMemory<byte> data)
    {
        Size = size;
        Tag = tag;
        Count = count;
        Data = data;
    }

    public Rreaddir(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Data = data.Slice(offset, (int)Count).ToArray();
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rreaddir);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Count); offset += 4;
        if (!Data.IsEmpty)
        {
            Data.Span.CopyTo(span.Slice(offset));
        }
    }
}

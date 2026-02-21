namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rreadlink : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rreadlink;
    public ushort Tag { get; }
    public string Target { get; }

    public Rreadlink(uint size, ushort tag, string target)
    {
        Size = size;
        Tag = tag;
        Target = target;
    }

    public Rreadlink(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Target = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rreadlink);
        int offset = NinePConstants.HeaderSize;
        span.WriteString(Target, ref offset);
    }
}

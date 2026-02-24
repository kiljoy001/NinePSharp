using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Rlerror: ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rlerror;
    public ushort Tag { get; }
    public uint Ecode { get; }

    public Rlerror(ushort tag, uint ecode)
    {
        Size = NinePConstants.HeaderSize + 4;
        Tag = tag;
        Ecode = ecode;
    }

    public Rlerror(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Ecode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Ecode);
    }
}

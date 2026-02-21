using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rcreate tag[2] qid[13] iounit[4]
public readonly struct Rcreate : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rcreate;
    public ushort Tag { get; }

    public Qid Qid { get; }
    public uint Iounit { get; }

    public Rcreate(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Qid = data.ReadQid(ref offset);
        Iounit = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        data.WriteQid(Qid, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Iounit);
    }
}

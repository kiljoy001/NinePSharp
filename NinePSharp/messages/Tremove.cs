using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Tremove tag[2] fid[4]
public readonly struct Tremove : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tremove;
    public ushort Tag { get; }

    public uint Fid { get; }

    public Tremove(ushort tag, uint fid)
    {
        Tag = tag;
        Fid = fid;
        Size = NinePConstants.HeaderSize + 4;
    }

    public Tremove(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
    }
}

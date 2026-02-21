using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Tread tag[2] fid[4] offset[8] count[4]
public readonly struct Tread : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tread;
    public ushort Tag { get; }

    public uint Fid { get; }
    public ulong Offset { get; }
    public uint Count { get; }

    public Tread(ushort tag, uint fid, ulong offset, uint count)
    {
        Tag = tag;
        Fid = fid;
        Offset = offset;
        Count = count;
        Size = NinePConstants.HeaderSize + 4 + 8 + 4;
    }

    public Tread(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Offset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
        offset += 8;

        Count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Offset);
        offset += 8;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Count);
    }
}

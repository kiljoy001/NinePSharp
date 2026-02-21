using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Twrite tag[2] fid[4] offset[8] count[4] data[count]
public readonly struct Twrite : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Twrite;
    public ushort Tag { get; }

    public uint Fid { get; }
    public ulong Offset { get; }
    public uint Count { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public Twrite(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        Size = BinaryPrimitives.ReadUInt32LittleEndian(span[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Fid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        Offset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        Count = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        // Zero-copy: Reference the existing memory slice
        Data = data.Slice(offset, (int)Count);
    }
    
    public Twrite(uint size, ushort tag, uint fid, ulong offset, uint count, ReadOnlyMemory<byte> data)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Offset = offset;
        Count = count;
        Data = data;
    }

    public Twrite(ushort tag, uint fid, ulong offset, ReadOnlyMemory<byte> data)
    {
        Tag = tag;
        Fid = fid;
        Offset = offset;
        Data = data;
        Count = (uint)data.Length;
        Size = (uint)(NinePConstants.HeaderSize + 4 + 8 + 4 + data.Length);
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
        offset += 4;

        Data.Span.CopyTo(data.Slice(offset, (int)Count));
    }
}

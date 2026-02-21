using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rread tag[2] count[4] data[count]
public readonly struct Rread : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rread;
    public ushort Tag { get; }

    public uint Count { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public Rread(ushort tag, byte[] data)
    {
        Tag = tag;
        Data = data ?? Array.Empty<byte>();
        Count = (uint)Data.Length;
        Size = (uint)(NinePConstants.HeaderSize + 4 + Data.Length);
    }

    public Rread(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Data = data.Slice(offset, (int)Count).ToArray(); // Need an allocation for Memory representing the read buffer since Span can't be held in struct easily if returning from method
    }
    
    public Rread(uint size, ushort tag, uint count, ReadOnlyMemory<byte> data)
    {
        Size = size;
        Tag = tag;
        Count = count;
        Data = data;
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Count);
        offset += 4;

        Data.Span.CopyTo(data.Slice(offset, (int)Count));
    }
}

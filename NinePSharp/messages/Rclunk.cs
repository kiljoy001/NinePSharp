using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rclunk tag[2]
public readonly struct Rclunk : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rclunk;
    public ushort Tag { get; }

    public Rclunk(ushort tag)
    {
        Tag = tag;
        Size = NinePConstants.HeaderSize;
    }

    public Rclunk(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
    }
}

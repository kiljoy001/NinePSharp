namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tunlinkat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tunlinkat;
    public ushort Tag { get; }
    public uint DirFd { get; }
    public string Name { get; }
    public uint Flags { get; }

    public Tunlinkat(uint size, ushort tag, uint dirFd, string name, uint flags)
    {
        Size = size;
        Tag = tag;
        DirFd = dirFd;
        Name = name;
        Flags = flags;
    }

    public Tunlinkat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        DirFd = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Name = data.ReadString(ref offset);
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tunlinkat);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), DirFd); offset += 4;
        span.WriteString(Name, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Flags);
    }
}

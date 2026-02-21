using System.Buffers.Binary;
using NinePSharp.Interfaces;
using NinePSharp.Constants;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Tversion : ISerializable
{
    public uint Size {get;}
    public MessageTypes Type => MessageTypes.Tversion;
    public ushort Tag {get;}
    public uint MSize {get;}
    public string Version {get;}

    public Tversion(ushort tag, uint msize, string version)
    {
        Tag = tag;
        MSize = msize;
        Version = version;
        Size = (uint)(NinePConstants.HeaderSize + 4 + 2 + System.Text.Encoding.UTF8.GetByteCount(version));
    }

    public Tversion(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        MSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        Version = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), MSize);
        offset += 4;
        data.WriteString(Version, ref offset);
    }
}
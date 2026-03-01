using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Tcreate tag[2] fid[4] name[s] perm[4] mode[1]
public readonly struct Tcreate : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tcreate;
    public ushort Tag { get; }

    public uint Fid { get; }
    public string Name { get; }
    public uint Perm { get; }
    public byte Mode { get; }

    public Tcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        Tag = tag;
        Fid = fid;
        Name = name;
        Perm = perm;
        Mode = mode;
        Size = (uint)(NinePConstants.HeaderSize + 4 + 2 + Encoding.UTF8.GetByteCount(name) + 4 + 1);
    }

    public Tcreate(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Name = data.ReadString(ref offset);

        Perm = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Mode = data[offset];
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        data.WriteString(Name, ref offset);

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Perm);
        offset += 4;

        data[offset] = Mode;
    }
}

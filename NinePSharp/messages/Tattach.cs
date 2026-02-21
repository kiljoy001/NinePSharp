using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Tattach tag[2] fid[4] afid[4] uname[s] aname[s]
public readonly struct Tattach : ISerializable
{
    public uint Size {get;}
    public MessageTypes Type => MessageTypes.Tattach;
    public ushort Tag {get;}

    public uint Fid {get;}
    public uint Afid {get;}
    public string Uname {get;}
    public string Aname {get;}
    public uint? NUname {get;} // 9P2000.u extension

    public Tattach(ushort tag, uint fid, uint afid, string uname, string aname)
    {
        Tag = tag;
        Fid = fid;
        Afid = afid;
        Uname = uname;
        Aname = aname;
        NUname = null;
        Size = (uint)(NinePConstants.HeaderSize + 4 + 4 + 2 + System.Text.Encoding.UTF8.GetByteCount(uname) + 2 + System.Text.Encoding.UTF8.GetByteCount(aname));
    }

    public Tattach(ReadOnlySpan<byte> data, bool is9u = false)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        
        Afid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Uname = data.ReadString(ref offset);
        Aname = data.ReadString(ref offset);

        if (is9u && offset + 4 <= data.Length)
        {
            NUname = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
        }
        else
        {
            NUname = null;
        }
    }

    public void WriteTo(Span<byte> data, bool is9u = false)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Afid);
        offset += 4;

        data.WriteString(Uname, ref offset);
        data.WriteString(Aname, ref offset);

        if (is9u && NUname.HasValue)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NUname.Value);
            offset += 4;
        }
    }

    void ISerializable.WriteTo(Span<byte> data) => WriteTo(data, NUname.HasValue);
}

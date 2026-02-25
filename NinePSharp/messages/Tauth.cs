using System;
using System.Buffers.Binary;
using NinePSharp.Interfaces;
using NinePSharp.Constants;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Tauth : ISerializable
{
    public uint Size {get;}
    public MessageTypes Type => MessageTypes.Tauth;
    public ushort Tag {get;}
    public uint Afid {get;}
    public string Uname {get;}
    public string Aname {get;}
    public uint? NUname {get;} // 9P2000.u extension

    public Tauth(ReadOnlySpan<byte> data, bool is9u = false)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
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

    public Tauth(ushort tag, uint afid, string? uname, string? aname, uint? nuname = null)
    {
        Tag = tag;
        Afid = afid;
        Uname = uname ?? string.Empty;
        Aname = aname ?? string.Empty;
        NUname = nuname;
        Size = (uint)(NinePConstants.HeaderSize + 4 + 
                      2 + System.Text.Encoding.UTF8.GetByteCount(Uname) + 
                      2 + System.Text.Encoding.UTF8.GetByteCount(Aname) + 
                      (nuname.HasValue ? 4 : 0));
    }

    public void WriteTo(Span<byte> data, bool is9u = false)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
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

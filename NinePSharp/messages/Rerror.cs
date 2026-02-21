using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Rerror: ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rerror;
    public ushort Tag { get; }
    public string Ename { get; }
    public uint? Ecode { get; } // 9P2000.u extension

    public Rerror(ushort tag, string ename, uint? ecode = null)
    {
        Tag = tag;
        Ename = ename;
        Ecode = ecode;
        
        uint size = NinePConstants.HeaderSize;
        size += (uint)(2 + System.Text.Encoding.UTF8.GetByteCount(ename));
        if (ecode.HasValue) size += 4;
        Size = size;
    }

    public Rerror(ushort tag, string ename)
    {
        Tag = tag;
        Ename = ename;
        Size = (uint)(NinePConstants.HeaderSize + 2 + System.Text.Encoding.UTF8.GetByteCount(ename));
    }

    public Rerror(ReadOnlySpan<byte> data, bool is9u = false)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Ename = data.ReadString(ref offset);

        if (is9u && offset + 4 <= data.Length)
        {
            Ecode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
        }
        else
        {
            Ecode = null;
        }
    }
    
    public void WriteTo(Span<byte> data, bool is9u = false)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;
        data.WriteString(Ename, ref offset);

        if (is9u && Ecode.HasValue)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Ecode.Value);
            offset += 4;
        }
    }

    void ISerializable.WriteTo(Span<byte> data) => WriteTo(data, Ecode.HasValue);
}
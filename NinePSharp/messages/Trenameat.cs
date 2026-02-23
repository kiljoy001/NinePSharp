namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Trenameat : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Trenameat;
    public ushort Tag { get; }
    public uint OldDirFid { get; }
    public string OldName { get; }
    public uint NewDirFid { get; }
    public string NewName { get; }

    public Trenameat(uint size, ushort tag, uint oldDirFid, string oldName, uint newDirFid, string newName)
    {
        Size = size;
        Tag = tag;
        OldDirFid = oldDirFid;
        OldName = oldName;
        NewDirFid = newDirFid;
        NewName = newName;
    }

    public Trenameat(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        OldDirFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        OldName = data.ReadString(ref offset);
        NewDirFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        NewName = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Trenameat);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), OldDirFid); offset += 4;
        span.WriteString(OldName, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), NewDirFid); offset += 4;
        span.WriteString(NewName, ref offset);
    }
}

namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tgetlock : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tgetlock;
    public ushort Tag { get; }
    public uint Fid { get; }
    public byte LockType { get; }
    public ulong Start { get; }
    public ulong Length { get; }
    public uint ProcId { get; }
    public string ClientId { get; }

    public Tgetlock(uint size, ushort tag, uint fid, byte lockType, ulong start, ulong length, uint procId, string clientId)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        LockType = lockType;
        Start = start;
        Length = length;
        ProcId = procId;
        ClientId = clientId;
    }

    public Tgetlock(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        LockType = data[offset++];
        Start = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Length = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        ProcId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        ClientId = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tgetlock);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        span[offset++] = LockType;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), Start); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), Length); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), ProcId); offset += 4;
        span.WriteString(ClientId, ref offset);
    }
}

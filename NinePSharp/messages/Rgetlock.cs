namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Rgetlock : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rgetlock;
    public ushort Tag { get; }
    public byte LockType { get; }
    public ulong Start { get; }
    public ulong Length { get; }
    public uint ProcId { get; }
    public string ClientId { get; }

    public Rgetlock(uint size, ushort tag, byte lockType, ulong start, ulong length, uint procId, string clientId)
    {
        Size = size;
        Tag = tag;
        LockType = lockType;
        Start = start;
        Length = length;
        ProcId = procId;
        ClientId = clientId;
    }

    public Rgetlock(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        LockType = data[offset++];
        Start = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Length = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        ProcId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        ClientId = data.ReadString(ref offset);
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Rgetlock);
        int offset = NinePConstants.HeaderSize;
        span[offset++] = LockType;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), Start); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), Length); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), ProcId); offset += 4;
        span.WriteString(ClientId, ref offset);
    }
}

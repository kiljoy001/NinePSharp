namespace NinePSharp.Messages;

using System;
using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

public readonly struct Tsetattr : IMessage
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Tsetattr;
    public ushort Tag { get; }
    public uint Fid { get; }
    public uint Valid { get; }
    public uint Mode { get; }
    public uint Uid { get; }
    public uint Gid { get; }
    public ulong FileSize { get; }
    public ulong AtimeSec { get; }
    public ulong AtimeNsec { get; }
    public ulong MtimeSec { get; }
    public ulong MtimeNsec { get; }

    public Tsetattr(uint size, ushort tag, uint fid, uint valid, uint mode, uint uid, uint gid, ulong fileSize, ulong atimeSec, ulong atimeNsec, ulong mtimeSec, ulong mtimeNsec)
    {
        Size = size;
        Tag = tag;
        Fid = fid;
        Valid = valid;
        Mode = mode;
        Uid = uid;
        Gid = gid;
        FileSize = fileSize;
        AtimeSec = atimeSec;
        AtimeNsec = atimeNsec;
        MtimeSec = mtimeSec;
        MtimeNsec = mtimeNsec;
    }

    public Tsetattr(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Valid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Uid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        FileSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        AtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        AtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        MtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        MtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
    }

    public void WriteTo(Span<byte> span)
    {
        span.WriteHeaders(Size, Tag, MessageTypes.Tsetattr);
        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Fid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Valid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Mode); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Uid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), Gid); offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), FileSize); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), AtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), AtimeNsec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), MtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), MtimeNsec);
    }
}

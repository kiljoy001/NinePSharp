using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rgetattr tag[2] valid[8] qid[13] mode[4] uid[4] gid[4] nlink[8] rdev[8] size[8] blksize[8] blocks[8] atime[16] mtime[16] ctime[16] btime[16] gen[8] data_version[8]
public readonly struct Rgetattr : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rgetattr;
    public ushort Tag { get; }

    public ulong Valid { get; }
    public Qid Qid { get; }
    public uint Mode { get; }
    public uint Uid { get; }
    public uint Gid { get; }
    public ulong Nlink { get; }
    public ulong Rdev { get; }
    public ulong DataSize { get; }
    public ulong BlkSize { get; }
    public ulong Blocks { get; }
    public ulong AtimeSec { get; }
    public ulong AtimeNsec { get; }
    public ulong MtimeSec { get; }
    public ulong MtimeNsec { get; }
    public ulong CtimeSec { get; }
    public ulong CtimeNsec { get; }
    public ulong BtimeSec { get; }
    public ulong BtimeNsec { get; }
    public ulong Gen { get; }
    public ulong DataVersion { get; }

    public Rgetattr(ushort tag, ulong valid, Qid qid, uint mode, uint uid, uint gid, ulong nlink, ulong rdev, ulong dataSize, ulong blkSize, ulong blocks, ulong atimeSec, ulong atimeNsec, ulong mtimeSec, ulong mtimeNsec, ulong ctimeSec, ulong ctimeNsec, ulong btimeSec, ulong btimeNsec, ulong gen, ulong dataVersion)
    {
        Tag = tag;
        Valid = valid;
        Qid = qid;
        Mode = mode;
        Uid = uid;
        Gid = gid;
        Nlink = nlink;
        Rdev = rdev;
        DataSize = dataSize;
        BlkSize = blkSize;
        Blocks = blocks;
        AtimeSec = atimeSec;
        AtimeNsec = atimeNsec;
        MtimeSec = mtimeSec;
        MtimeNsec = mtimeNsec;
        CtimeSec = ctimeSec;
        CtimeNsec = ctimeNsec;
        BtimeSec = btimeSec;
        BtimeNsec = btimeNsec;
        Gen = gen;
        DataVersion = dataVersion;
        Size = NinePConstants.HeaderSize + 8 + 13 + 4 + 4 + 4 + 8 + 8 + 8 + 8 + 8 + 16 + 16 + 16 + 16 + 8 + 8;
    }

    public Rgetattr(ushort tag, ulong valid, Qid qid, uint mode) 
        : this(tag, valid, qid, mode, 0, 0, 1, 0, 0, 4096, 0, 
               (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 
               (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 
               (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 
               0, 0, 0, 0)
    {
    }

    public Rgetattr(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        Valid = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Qid = data.ReadQid(ref offset);
        Mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Uid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Nlink = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Rdev = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        DataSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        BlkSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Blocks = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        AtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        AtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        MtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        MtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        CtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        CtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        BtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        BtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Gen = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        DataVersion = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Valid); offset += 8;
        data.WriteQid(Qid, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Mode); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Uid); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Gid); offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Nlink); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Rdev); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), DataSize); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), BlkSize); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Blocks); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), AtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), AtimeNsec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), MtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), MtimeNsec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), CtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), CtimeNsec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), BtimeSec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), BtimeNsec); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Gen); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), DataVersion); offset += 8;
    }
}

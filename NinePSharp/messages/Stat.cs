using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;
using System.Text;

namespace NinePSharp.Messages;

// stat[n]
// size[2] type[2] dev[4] qid[13] mode[4] atime[4] mtime[4] length[8] name[s] uid[s] gid[s] muid[s] extension[s] n_uid[4] n_gid[4] n_muid[4]
public readonly struct Stat
{
    public ushort Size { get; }
    public ushort Type { get; }
    public uint Dev { get; }
    public Qid Qid { get; }
    public uint Mode { get; }
    public uint Atime { get; }
    public uint Mtime { get; }
    public ulong Length { get; }
    public string Name { get; }
    public string Uid { get; }
    public string Gid { get; }
    public string Muid { get; }
    
    // 9P2000.u
    public string? Extension { get; }
    public uint? NUid { get; }
    public uint? NGid { get; }
    public uint? NMuid { get; }

    public static ushort CalculateSize(string name, string uid, string gid, string muid, string? extension = null)
    {
        int size = 2 + 2 + 4 + 13 + 4 + 4 + 4 + 8; // size[2] type[2] dev[4] qid[13] mode[4] atime[4] mtime[4] length[8]
        size += 2 + Encoding.UTF8.GetByteCount(name);
        size += 2 + Encoding.UTF8.GetByteCount(uid);
        size += 2 + Encoding.UTF8.GetByteCount(gid);
        size += 2 + Encoding.UTF8.GetByteCount(muid);
        
        if (extension != null)
        {
            size += 2 + Encoding.UTF8.GetByteCount(extension);
            size += 4 + 4 + 4; // n_uid[4] n_gid[4] n_muid[4]
        }
        
        return (ushort)size;
    }
    
    public Stat(ushort size, ushort type, uint dev, Qid qid, uint mode, uint atime, uint mtime, ulong length, string name, string uid, string gid, string muid, string? extension = null, uint? nUid = null, uint? nGid = null, uint? nMuid = null)
    {
        Size = size == 0 ? CalculateSize(name, uid, gid, muid, extension) : size;
        Type = type;
        Dev = dev;
        Qid = qid;
        Mode = mode;
        Atime = atime;
        Mtime = mtime;
        Length = length;
        Name = name;
        Uid = uid;
        Gid = gid;
        Muid = muid;
        Extension = extension;
        NUid = nUid;
        NGid = nGid;
        NMuid = nMuid;
    }

    public Stat(ReadOnlySpan<byte> data, ref int offset)
    {
        int initialOffset = offset;
        Size = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) + 2);
        offset += 2;
        
        Type = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        
        Dev = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        
        Qid = data.ReadQid(ref offset);
        
        Mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        
        Atime = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        
        Mtime = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        
        Length = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
        offset += 8;
        
        Name = data.ReadString(ref offset);
        Uid = data.ReadString(ref offset);
        Gid = data.ReadString(ref offset);
        Muid = data.ReadString(ref offset);
        
        // Check for 9P2000.u extensions
        if (offset - initialOffset < Size)
        {
            Extension = data.ReadString(ref offset);
            NUid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
            NGid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
            NMuid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
        }
    }
    
    public void WriteTo(Span<byte> data, ref int offset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), (ushort)(Size - 2));
        offset += 2;
        
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), Type);
        offset += 2;
        
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Dev);
        offset += 4;
        
        data.WriteQid(Qid, ref offset);
        
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Mode);
        offset += 4;
        
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Atime);
        offset += 4;
        
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Mtime);
        offset += 4;
        
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Length);
        offset += 8;
        
        data.WriteString(Name, ref offset);
        data.WriteString(Uid, ref offset);
        data.WriteString(Gid, ref offset);
        data.WriteString(Muid, ref offset);
        
        if (Extension != null)
        {
            data.WriteString(Extension, ref offset);
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NUid ?? 0);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NGid ?? 0);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NMuid ?? 0);
            offset += 4;
        }
    }
}

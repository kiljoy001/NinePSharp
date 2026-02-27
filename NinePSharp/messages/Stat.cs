using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;
using System.Text;

namespace NinePSharp.Messages;

/// <summary>
/// 9P Stat structure.
/// Layout (Standard 9P2000):
/// size[2] type[2] dev[4] qid[13] mode[4] atime[4] mtime[4] length[8] name[s] uid[s] gid[s] muid[s]
/// 
/// Layout (9P2000.u additions):
/// extension[s] n_uid[4] n_gid[4] n_muid[4]
/// </summary>
public readonly struct Stat
{
    public NinePDialect Dialect { get; }
    private bool Is9u => Dialect == NinePDialect.NineP2000U || Dialect == NinePDialect.NineP2000L;
    
    public ushort Size { get; } // Total size including the 2-byte header
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

    public static ushort CalculateSize(string? name, string? uid, string? gid, string? muid, NinePDialect dialect, string? extension = null)
    {
        bool is9u = dialect == NinePDialect.NineP2000U || dialect == NinePDialect.NineP2000L;
        // 1. Fixed fields (excluding size[2] itself):
        // type[2] + dev[4] + qid[13] + mode[4] + atime[4] + mtime[4] + length[8] = 39 bytes
        int payloadSize = 39;

        // 2. Variable string fields: 2 bytes length + UTF8 bytes
        payloadSize += 2 + Encoding.UTF8.GetByteCount(name ?? "");
        payloadSize += 2 + Encoding.UTF8.GetByteCount(uid ?? "");
        payloadSize += 2 + Encoding.UTF8.GetByteCount(gid ?? "");
        payloadSize += 2 + Encoding.UTF8.GetByteCount(muid ?? "");
        
        // 3. 9P2000.u extensions
        if (is9u)
        {
            payloadSize += 2 + Encoding.UTF8.GetByteCount(extension ?? "");
            payloadSize += 4 + 4 + 4; // n_uid[4] n_gid[4] n_muid[4]
        }
        
        // Total size is payload + the 2 bytes for the size field itself
        return (ushort)(payloadSize + 2);
    }
    
    public Stat(ushort size, ushort type, uint dev, Qid qid, uint mode, uint atime, uint mtime, ulong length, string? name, string? uid, string? gid, string? muid, NinePDialect dialect = NinePDialect.NineP2000, string? extension = null, uint? nUid = null, uint? nGid = null, uint? nMuid = null)
    {
        Dialect = dialect;
        Name = name ?? "";
        Uid = uid ?? "";
        Gid = gid ?? "";
        Muid = muid ?? "";
        Extension = extension;
        
        Type = type;
        Dev = dev;
        Qid = qid;
        Mode = mode;
        Atime = atime;
        Mtime = mtime;
        Length = length;

        bool is9u = dialect == NinePDialect.NineP2000U || dialect == NinePDialect.NineP2000L;
        NUid = nUid ?? (is9u ? uint.MaxValue : (uint?)null);
        NGid = nGid ?? (is9u ? uint.MaxValue : (uint?)null);
        NMuid = nMuid ?? (is9u ? uint.MaxValue : (uint?)null);

        Size = size == 0 ? CalculateSize(Name, Uid, Gid, Muid, dialect, Extension) : size;
    }

    public Stat(ReadOnlySpan<byte> data, ref int offset, NinePDialect dialect = NinePDialect.NineP2000)
    {
        Dialect = dialect;
        bool is9u = dialect == NinePDialect.NineP2000U || dialect == NinePDialect.NineP2000L;
        int startOffset = offset;
        
        // size[2] header
        ushort dataLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        Size = (ushort)(dataLength + 2);
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
        
        Extension = null;
        NUid = null;
        NGid = null;
        NMuid = null;

        if (is9u && (offset - startOffset < Size))
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
        int startOffset = offset;

        // 1. Write size[2] (length of following data)
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), (ushort)(Size - 2));
        offset += 2;
        
        // 2. Fixed fields
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
        
        // 3. String fields
        data.WriteString(Name, ref offset);
        data.WriteString(Uid, ref offset);
        data.WriteString(Gid, ref offset);
        data.WriteString(Muid, ref offset);
        
        // 4. Unix Extensions
        if (Is9u)
        {
            data.WriteString(Extension ?? "", ref offset);
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NUid ?? uint.MaxValue);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NGid ?? uint.MaxValue);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NMuid ?? uint.MaxValue);
            offset += 4;
        }

        // Integrity Check
        if (offset - startOffset != Size)
        {
            Console.WriteLine($"[Stat CRITICAL Mismatch] Name={Name}, ExpectedSize={Size}, ActualWritten={offset - startOffset}");
        }
    }
}

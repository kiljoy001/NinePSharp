using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Rstatfs tag[2] type[4] bsize[4] blocks[8] bfree[8] bavail[8] files[8] ffree[8] fsid[8] namelen[4]
public readonly struct Rstatfs : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Rstatsfs;
    public ushort Tag { get; }

    public uint FsType { get; }
    public uint BSize { get; }
    public ulong Blocks { get; }
    public ulong BFree { get; }
    public ulong BAvail { get; }
    public ulong Files { get; }
    public ulong FFree { get; }
    public ulong FsId { get; }
    public uint NameLen { get; }

    public Rstatfs(ushort tag, uint fsType, uint bSize, ulong blocks, ulong bFree, ulong bAvail, ulong files, ulong fFree, ulong fsId, uint nameLen)
    {
        Tag = tag;
        FsType = fsType;
        BSize = bSize;
        Blocks = blocks;
        BFree = bFree;
        BAvail = bAvail;
        Files = files;
        FFree = fFree;
        FsId = fsId;
        NameLen = nameLen;
        Size = NinePConstants.HeaderSize + 4 + 4 + 8 + 8 + 8 + 8 + 8 + 8 + 4;
    }

    public Rstatfs(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;
        FsType = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        BSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
        Blocks = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        BFree = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        BAvail = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        Files = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        FFree = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        FsId = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)); offset += 8;
        NameLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)); offset += 4;
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), FsType); offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), BSize); offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Blocks); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), BFree); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), BAvail); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), Files); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), FFree); offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), FsId); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NameLen); offset += 4;
    }
}

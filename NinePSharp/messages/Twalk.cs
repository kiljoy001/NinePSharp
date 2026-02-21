using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

// size[4] Twalk tag[2] fid[4] newfid[4] nwname[2] nwname*(wname[s])
public readonly struct Twalk : ISerializable
{
    public uint Size { get; }
    public MessageTypes Type => MessageTypes.Twalk;
    public ushort Tag { get; }

    public uint Fid { get; }
    public uint NewFid { get; }
    public string[] Wname { get; }

    public Twalk(ushort tag, uint fid, uint newFid, string[] wname)
    {
        Tag = tag;
        Fid = fid;
        NewFid = newFid;
        Wname = wname;
        // Size will be calculated during write if needed, 
        // but let's set a minimal valid size for now
        Size = (uint)(NinePConstants.HeaderSize + 4 + 4 + 2 + (wname?.Sum(s => 2 + s.Length) ?? 0));
    }

    public Twalk(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));

        int offset = NinePConstants.HeaderSize;

        Fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        NewFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        ushort nwname = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        Wname = new string[nwname];
        for (int i = 0; i < nwname; i++)
        {
            Wname[i] = data.ReadString(ref offset);
        }
    }

    public void WriteTo(Span<byte> data)
    {
        data.WriteHeaders(Size, Tag, Type);
        int offset = NinePConstants.HeaderSize;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), Fid);
        offset += 4;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), NewFid);
        offset += 4;

        ushort nwname = (ushort)(Wname?.Length ?? 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), nwname);
        offset += 2;

        for (int i = 0; i < nwname; i++)
        {
            data.WriteString(Wname![i], ref offset);
        }
    }
}

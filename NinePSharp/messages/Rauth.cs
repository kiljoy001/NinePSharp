using System.Buffers.Binary;
using NinePSharp.Interfaces;
using NinePSharp.Constants;
using NinePSharp.Protocol;

namespace NinePSharp.Messages;

public readonly struct Rauth: ISerializable
{
    public uint Size {get;}
    public MessageTypes Type => MessageTypes.Rauth;
    public ushort Tag { get; }
    public Qid Aqid { get; }
    
    public Rauth(ushort tag, Qid aqid)
    {
        Tag = tag;
        Aqid = aqid;
        Size = NinePConstants.HeaderSize + 13; // same as Rattach — qid is 13 bytes
    }

    public Rauth(ReadOnlySpan<byte> data)
    {
        Size = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        Tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        int offset = NinePConstants.HeaderSize;
        Aqid = data.ReadQid(ref offset);
    }

    public void WriteTo(Span<byte> data)
    {
        const uint dataSize = 20;
        data.WriteHeaders(dataSize,Tag,Type);
        int offset = NinePConstants.HeaderSize;
        data.WriteQid(this.Aqid, ref offset);
    }
}
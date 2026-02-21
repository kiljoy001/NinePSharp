using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class ConnectionTests : TestBase
{
    [Fact]
    public void Test_Tversion_Roundtrip()
    {
        string version = "9P2000";
        ushort tag = NinePConstants.NoTag;
        uint msize = NinePConstants.DefaultMSize;
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + Encoding.UTF8.GetByteCount(version));
        
        RoundTripTest<Tversion>(size, tag, 
            buffer => new Tversion(buffer),
            msg => { Assert.Equal(msize, msg.MSize); Assert.Equal(version, msg.Version); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tversion);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), msize); offset += 4;
                buf.AsSpan().WriteString(version, ref offset);
                return new Tversion(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rversion_Roundtrip()
    {
        string version = "9P2000.u";
        ushort tag = NinePConstants.NoTag;
        uint msize = NinePConstants.DefaultMSize;
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + Encoding.UTF8.GetByteCount(version));
        
        RoundTripTest<Rversion>(size, tag, 
            buffer => new Rversion(buffer),
            msg => { Assert.Equal(msize, msg.MSize); Assert.Equal(version, msg.Version); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rversion);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), msize); offset += 4;
                buf.AsSpan().WriteString(version, ref offset);
                return new Rversion(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tauth_Roundtrip()
    {
        ushort tag = 1;
        uint afid = 2;
        string uname = "scott";
        string aname = "tree";
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + Encoding.UTF8.GetByteCount(uname) + 2 + Encoding.UTF8.GetByteCount(aname));
        
        RoundTripTest<Tauth>(size, tag, 
            buffer => new Tauth(buffer, is9u: false),
            msg => { Assert.Equal(afid, msg.Afid); Assert.Equal(uname, msg.Uname); Assert.Equal(aname, msg.Aname); Assert.False(msg.NUname.HasValue); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tauth);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), afid); offset += 4;
                buf.AsSpan().WriteString(uname, ref offset);
                buf.AsSpan().WriteString(aname, ref offset);
                return new Tauth(buf, is9u: false);
            },
            (msg, span) => msg.WriteTo(span, is9u: false));
    }

    [Fact]
    public void Test_Rauth_Roundtrip()
    {
        ushort tag = 1;
        var qid = new Qid(QidType.QTAUTH, 1, 100);
        uint size = (uint)(NinePConstants.HeaderSize + 13);
        RoundTripTest<Rauth>(size, tag, 
            buffer => new Rauth(buffer),
            msg => { Assert.Equal(qid.Type, msg.Aqid.Type); Assert.Equal(qid.Version, msg.Aqid.Version); Assert.Equal(qid.Path, msg.Aqid.Path); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rauth);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                return new Rauth(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tattach_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 1;
        uint afid = NinePConstants.NoFid;
        string uname = "scott";
        string aname = "tree";
        uint size = (uint)(NinePConstants.HeaderSize + 8 + 2 + Encoding.UTF8.GetByteCount(uname) + 2 + Encoding.UTF8.GetByteCount(aname));
        
        RoundTripTest<Tattach>(size, tag, 
            buffer => new Tattach(buffer, is9u: false),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(afid, msg.Afid); Assert.Equal(uname, msg.Uname); Assert.Equal(aname, msg.Aname); Assert.False(msg.NUname.HasValue); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tattach);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), afid); offset += 4;
                buf.AsSpan().WriteString(uname, ref offset);
                buf.AsSpan().WriteString(aname, ref offset);
                return new Tattach(buf, is9u: false);
            },
            (msg, span) => msg.WriteTo(span, is9u: false));
    }

    [Fact]
    public void Test_Rattach_Roundtrip()
    {
        ushort tag = 1;
        var qid = new Qid(QidType.QTDIR, 1, 100);
        uint size = (uint)(NinePConstants.HeaderSize + 13);
        RoundTripTest<Rattach>(size, tag, 
            buffer => new Rattach(buffer),
            msg => { Assert.Equal(qid.Type, msg.Qid.Type); Assert.Equal(qid.Version, msg.Qid.Version); Assert.Equal(qid.Path, msg.Qid.Path); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rattach);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                return new Rattach(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rerror_Roundtrip()
    {
        ushort tag = 1;
        string errName = "file not found";
        uint size = (uint)(NinePConstants.HeaderSize + 2 + Encoding.UTF8.GetByteCount(errName));
        
        RoundTripTest<Rerror>(size, tag, 
            buffer => new Rerror(buffer, is9u: false),
            msg => { Assert.Equal(errName, msg.Ename); Assert.False(msg.Ecode.HasValue); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rerror);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteString(errName, ref offset);
                return new Rerror(buf, is9u: false);
            },
            (msg, span) => msg.WriteTo(span, is9u: false));
    }

    [Fact]
    public void Test_Rlerror_Roundtrip()
    {
        ushort tag = 1;
        uint ecode = 12;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Rlerror>(size, tag, 
            buffer => new Rlerror(buffer),
            msg => { Assert.Equal(ecode, msg.Ecode); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rlerror);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), ecode);
                return new Rlerror(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }
}

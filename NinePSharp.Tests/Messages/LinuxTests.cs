using NinePSharp.Constants;
using System.Text;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class LinuxTests : TestBase
{
    [Fact]
    public void Test_Tstatfs_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Tstatfs>(size, tag,
            buffer => new Tstatfs(buffer),
            msg => { Assert.Equal(fid, msg.Fid); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tstatfs);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
                return new Tstatfs(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rstatfs_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize + 4 + 4 + 8 + 8 + 8 + 8 + 8 + 8 + 4;
        RoundTripTest<Rstatfs>(size, tag,
            buffer => new Rstatfs(buffer),
            msg => { Assert.Equal(1u, msg.FsType); Assert.Equal(4096u, msg.BSize); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rstatsfs);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 1); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 4096); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 100); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 50); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 40); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 200); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 150); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 0x123456); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 255);
                return new Rstatfs(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tlopen_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint flags = 3;
        uint size = NinePConstants.HeaderSize + 4 + 4;
        RoundTripTest<Tlopen>(size, tag,
            buffer => new Tlopen(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(flags, msg.Flags); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tlopen);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), flags);
                return new Tlopen(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rlopen_Roundtrip()
    {
        ushort tag = 1;
        Qid qid = new Qid((QidType)1, 2, 3);
        uint iounit = 8192;
        uint size = NinePConstants.HeaderSize + 13 + 4;
        RoundTripTest<Rlopen>(size, tag,
            buffer => new Rlopen(buffer),
            msg => { Assert.Equal(qid.Type, msg.Qid.Type); Assert.Equal(iounit, msg.Iounit); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.RLopen);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), iounit);
                return new Rlopen(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tlcreate_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        string name = "test.txt";
        uint flags = 3;
        uint mode = 4;
        uint gid = 5;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + 4 + 4 + 4;
        RoundTripTest<Tlcreate>(size, tag,
            buffer => new Tlcreate(buffer),
            msg => { Assert.Equal(name, msg.Name); Assert.Equal(flags, msg.Flags); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tlcreate);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), flags); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), mode); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), gid);
                return new Tlcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rlcreate_Roundtrip()
    {
        ushort tag = 1;
        Qid qid = new Qid((QidType)1, 2, 3);
        uint iounit = 8192;
        uint size = NinePConstants.HeaderSize + 13 + 4;
        RoundTripTest<Rlcreate>(size, tag,
            buffer => new Rlcreate(buffer),
            msg => { Assert.Equal(qid.Path, msg.Qid.Path); Assert.Equal(iounit, msg.Iounit); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rlcreate);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), iounit);
                return new Rlcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tsymlink_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        string name = "link";
        string symtgt = "target";
        uint gid = 5;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(symtgt)) + 4;
        RoundTripTest<Tsymlink>(size, tag,
            buffer => new Tsymlink(buffer),
            msg => { Assert.Equal(name, msg.Name); Assert.Equal(symtgt, msg.Symtgt); Assert.Equal(gid, msg.Gid); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tsymlink);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                buf.AsSpan().WriteString(symtgt, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), gid);
                return new Tsymlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rsymlink_Roundtrip()
    {
        ushort tag = 1;
        Qid qid = new Qid((QidType)1, 2, 3);
        uint size = NinePConstants.HeaderSize + 13;
        RoundTripTest<Rsymlink>(size, tag,
            buffer => new Rsymlink(buffer),
            msg => { Assert.Equal(qid.Version, msg.Qid.Version); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rsymlink);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                return new Rsymlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tmknod_Roundtrip()
    {
        ushort tag = 1;
        uint dfid = 2;
        string name = "node";
        uint mode = 4;
        uint major = 5;
        uint minor = 6;
        uint gid = 7;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + 4 + 4 + 4 + 4;
        RoundTripTest<Tmknod>(size, tag,
            buffer => new Tmknod(buffer),
            msg => { Assert.Equal(name, msg.Name); Assert.Equal(minor, msg.Minor); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tmknod);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), dfid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), mode); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), major); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), minor); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), gid);
                return new Tmknod(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rmknod_Roundtrip()
    {
        ushort tag = 1;
        Qid qid = new Qid((QidType)1, 2, 3);
        uint size = NinePConstants.HeaderSize + 13;
        RoundTripTest<Rmknod>(size, tag,
            buffer => new Rmknod(buffer),
            msg => { Assert.Equal(qid.Path, msg.Qid.Path); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rmknod);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                return new Rmknod(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Trename_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint dfid = 3;
        string name = "newname";
        uint size = NinePConstants.HeaderSize + 4 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name));
        RoundTripTest<Trename>(size, tag,
            buffer => new Trename(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(name, msg.Name); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Trename);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), dfid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                return new Trename(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rrename_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rrename>(size, tag,
            buffer => new Rrename(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rrename);
                return new Rrename(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Treadlink_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Treadlink>(size, tag,
            buffer => new Treadlink(buffer),
            msg => { Assert.Equal(fid, msg.Fid); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Treadlink);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
                return new Treadlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rreadlink_Roundtrip()
    {
        ushort tag = 1;
        string target = "some/path";
        uint size = NinePConstants.HeaderSize + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(target));
        RoundTripTest<Rreadlink>(size, tag,
            buffer => new Rreadlink(buffer),
            msg => { Assert.Equal(target, msg.Target); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rreadlink);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteString(target, ref offset);
                return new Rreadlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tgetattr_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        ulong requestMask = 0x3FFF;
        uint size = NinePConstants.HeaderSize + 4 + 8;
        RoundTripTest<Tgetattr>(size, tag,
            buffer => new Tgetattr(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(requestMask, msg.RequestMask); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tgetattr);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), requestMask);
                return new Tgetattr(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rgetattr_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize + 8 + 13 + 4 + 4 + 4 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8 + 8;
        RoundTripTest<NinePSharp.Messages.Rgetattr>(size, tag,
            buffer => new NinePSharp.Messages.Rgetattr(buffer),
            msg => { Assert.Equal(0x3FFFu, msg.Valid); Assert.Equal(1u, msg.Mode); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rgetattr);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 0x3FFF); offset += 8;
                buf.AsSpan().WriteQid(new Qid((QidType)1, 2, 3), ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 1); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 2); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 3); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 4); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 5); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 6); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 7); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 8); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 9); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 10); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 11); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 12); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 13); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 14); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 15); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 16); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 17); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 18);
                return new NinePSharp.Messages.Rgetattr(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tsetattr_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4 + 4 + 4 + 4 + 4 + 8 + 8 + 8 + 8 + 8;
        RoundTripTest<Tsetattr>(size, tag,
            buffer => new Tsetattr(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(1u, msg.Valid); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tsetattr);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 1); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 2); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 3); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), 4); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 5); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 6); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 7); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 8); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), 9);
                return new Tsetattr(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rsetattr_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rsetattr>(size, tag,
            buffer => new Rsetattr(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rsetattr);
                return new Rsetattr(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Txattrwalk_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint newfid = 3;
        string name = "attr";
        uint size = NinePConstants.HeaderSize + 4 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name));
        RoundTripTest<Txattrwalk>(size, tag,
            buffer => new Txattrwalk(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(newfid, msg.NewFid); Assert.Equal(name, msg.Name); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Txattrwalk);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), newfid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                return new Txattrwalk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rxattrwalk_Roundtrip()
    {
        ushort tag = 1;
        ulong xattrSize = 100;
        uint size = NinePConstants.HeaderSize + 8;
        RoundTripTest<Rxattrwalk>(size, tag,
            buffer => new Rxattrwalk(buffer),
            msg => { Assert.Equal(xattrSize, msg.XattrSize); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rxattrwalk);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), xattrSize);
                return new Rxattrwalk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Txattrcreate_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        string name = "attr";
        ulong attrSize = 100;
        uint flags = 1;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + 8 + 4;
        RoundTripTest<Txattrcreate>(size, tag,
            buffer => new Txattrcreate(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(name, msg.Name); Assert.Equal(attrSize, msg.AttrSize); Assert.Equal(flags, msg.Flags); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Txattrcreate);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), attrSize); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), flags);
                return new Txattrcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rxattrcreate_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rxattrcreate>(size, tag,
            buffer => new Rxattrcreate(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rxattrcreate);
                return new Rxattrcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Treaddir_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        ulong offset_val = 100;
        uint count = 50;
        uint size = NinePConstants.HeaderSize + 4 + 8 + 4;
        RoundTripTest<Treaddir>(size, tag,
            buffer => new Treaddir(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(offset_val, msg.Offset); Assert.Equal(count, msg.Count); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Treaddir);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), offset_val); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count);
                return new Treaddir(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rreaddir_Roundtrip()
    {
        ushort tag = 1;
        uint count = 4;
        byte[] data = new byte[] { 1, 2, 3, 4 };
        uint size = NinePConstants.HeaderSize + 4 + count;
        RoundTripTest<Rreaddir>(size, tag,
            buffer => new Rreaddir(buffer),
            msg => { Assert.Equal(count, msg.Count); Assert.True(msg.Data.Span.SequenceEqual(data)); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rreaddir);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count); offset += 4;
                data.CopyTo(buf.AsSpan().Slice(offset));
                return new Rreaddir(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tfsync_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint datasync = 1;
        uint size = NinePConstants.HeaderSize + 4 + 4;
        RoundTripTest<Tfsync>(size, tag,
            buffer => new Tfsync(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(datasync, msg.Datasync); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tfsync);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), datasync);
                return new Tfsync(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rfsync_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rfsync>(size, tag,
            buffer => new Rfsync(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rfsync);
                return new Rfsync(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tlock_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        byte lockType = 3;
        uint flags = 4;
        ulong start = 100;
        ulong length = 200;
        uint procId = 5;
        string clientId = "client";
        uint size = NinePConstants.HeaderSize + 4 + 1 + 4 + 8 + 8 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(clientId));
        RoundTripTest<Tlock>(size, tag,
            buffer => new Tlock(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(lockType, msg.LockType); Assert.Equal(clientId, msg.ClientId); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tlock);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf[offset++] = lockType;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), flags); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), start); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), length); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), procId); offset += 4;
                buf.AsSpan().WriteString(clientId, ref offset);
                return new Tlock(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rlock_Roundtrip()
    {
        ushort tag = 1;
        byte status = 2;
        uint size = NinePConstants.HeaderSize + 1;
        RoundTripTest<Rlock>(size, tag,
            buffer => new Rlock(buffer),
            msg => { Assert.Equal(status, msg.Status); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rlock);
                buf[NinePConstants.HeaderSize] = status;
                return new Rlock(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tgetlock_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        byte lockType = 3;
        ulong start = 100;
        ulong length = 200;
        uint procId = 5;
        string clientId = "client";
        uint size = NinePConstants.HeaderSize + 4 + 1 + 8 + 8 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(clientId));
        RoundTripTest<Tgetlock>(size, tag,
            buffer => new Tgetlock(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(start, msg.Start); Assert.Equal(clientId, msg.ClientId); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tgetlock);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf[offset++] = lockType;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), start); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), length); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), procId); offset += 4;
                buf.AsSpan().WriteString(clientId, ref offset);
                return new Tgetlock(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rgetlock_Roundtrip()
    {
        ushort tag = 1;
        byte lockType = 3;
        ulong start = 100;
        ulong length = 200;
        uint procId = 5;
        string clientId = "client";
        uint size = NinePConstants.HeaderSize + 1 + 8 + 8 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(clientId));
        RoundTripTest<Rgetlock>(size, tag,
            buffer => new Rgetlock(buffer),
            msg => { Assert.Equal(lockType, msg.LockType); Assert.Equal(start, msg.Start); Assert.Equal(clientId, msg.ClientId); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rgetlock);
                int offset = NinePConstants.HeaderSize;
                buf[offset++] = lockType;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), start); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), length); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), procId); offset += 4;
                buf.AsSpan().WriteString(clientId, ref offset);
                return new Rgetlock(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tlink_Roundtrip()
    {
        ushort tag = 1;
        uint dfid = 2;
        uint fid = 3;
        string name = "link";
        uint size = NinePConstants.HeaderSize + 4 + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name));
        RoundTripTest<Tlink>(size, tag,
            buffer => new Tlink(buffer),
            msg => { Assert.Equal(dfid, msg.Dfid); Assert.Equal(fid, msg.Fid); Assert.Equal(name, msg.Name); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tlink);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), dfid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                return new Tlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rlink_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rlink>(size, tag,
            buffer => new Rlink(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rlink);
                return new Rlink(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tmkdir_Roundtrip()
    {
        ushort tag = 1;
        uint dfid = 2;
        string name = "dir";
        uint mode = 4;
        uint gid = 5;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + 4 + 4;
        RoundTripTest<Tmkdir>(size, tag,
            buffer => new Tmkdir(buffer),
            msg => { Assert.Equal(dfid, msg.Dfid); Assert.Equal(name, msg.Name); Assert.Equal(mode, msg.Mode); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tmkdir);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), dfid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), mode); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), gid);
                return new Tmkdir(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rmkdir_Roundtrip()
    {
        ushort tag = 1;
        Qid qid = new Qid((QidType)1, 2, 3);
        uint size = NinePConstants.HeaderSize + 13;
        RoundTripTest<Rmkdir>(size, tag,
            buffer => new Rmkdir(buffer),
            msg => { Assert.Equal(qid.Version, msg.Qid.Version); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rmkdir);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                return new Rmkdir(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Trenameat_Roundtrip()
    {
        ushort tag = 1;
        uint oldDirFid = 2;
        string oldName = "old";
        uint newDirFid = 3;
        string newName = "new";
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(oldName)) + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(newName));
        RoundTripTest<Trenameat>(size, tag,
            buffer => new Trenameat(buffer),
            msg => { Assert.Equal(oldDirFid, msg.OldDirFid); Assert.Equal(newName, msg.NewName); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Trenameat);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), oldDirFid); offset += 4;
                buf.AsSpan().WriteString(oldName, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), newDirFid); offset += 4;
                buf.AsSpan().WriteString(newName, ref offset);
                return new Trenameat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rrenameat_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rrenameat>(size, tag,
            buffer => new Rrenameat(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rrenameat);
                return new Rrenameat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tunlinkat_Roundtrip()
    {
        ushort tag = 1;
        uint dirFd = 2;
        string name = "file";
        uint flags = 1;
        uint size = NinePConstants.HeaderSize + 4 + (2 + (uint)System.Text.Encoding.UTF8.GetByteCount(name)) + 4;
        RoundTripTest<Tunlinkat>(size, tag,
            buffer => new Tunlinkat(buffer),
            msg => { Assert.Equal(dirFd, msg.DirFd); Assert.Equal(name, msg.Name); Assert.Equal(flags, msg.Flags); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tunlinkat);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), dirFd); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), flags);
                return new Tunlinkat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Runlinkat_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Runlinkat>(size, tag,
            buffer => new Runlinkat(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Runlinkat);
                return new Runlinkat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tflush_Roundtrip()
    {
        ushort tag = 1;
        ushort oldTag = 2;
        uint size = NinePConstants.HeaderSize + 2;
        RoundTripTest<Tflush>(size, tag,
            buffer => new Tflush(buffer),
            msg => { Assert.Equal(oldTag, msg.OldTag); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Tflush);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), oldTag);
                return new Tflush(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rflush_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rflush>(size, tag,
            buffer => new Rflush(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rflush);
                return new Rflush(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }
}

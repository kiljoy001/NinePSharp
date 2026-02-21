using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class LinearIOTests : TestBase
{
    [Fact]
    public void Test_Topen_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 1;
        byte mode = NinePConstants.OREAD;
        uint size = NinePConstants.HeaderSize + 5;
        RoundTripTest<Topen>(size, tag,
            buffer => new Topen(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(mode, msg.Mode); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Topen);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf[offset] = mode;
                return new Topen(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Ropen_Roundtrip()
    {
        ushort tag = 1;
        var qid = new Qid(QidType.QTFILE, 2, 200);
        uint iounit = 8192;
        uint size = NinePConstants.HeaderSize + 17;
        RoundTripTest<Ropen>(size, tag,
            buffer => new Ropen(buffer),
            msg => { Assert.Equal(qid.Type, msg.Qid.Type); Assert.Equal(qid.Path, msg.Qid.Path); Assert.Equal(iounit, msg.Iounit); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Ropen);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), iounit);
                return new Ropen(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tcreate_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        string name = "newfile.txt";
        uint perm = 0644;
        byte mode = NinePConstants.OWRITE;
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + Encoding.UTF8.GetByteCount(name) + 4 + 1);
        RoundTripTest<Tcreate>(size, tag,
            buffer => new Tcreate(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(name, msg.Name); Assert.Equal(perm, msg.Perm); Assert.Equal(mode, msg.Mode); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tcreate);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                buf.AsSpan().WriteString(name, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), perm); offset += 4;
                buf[offset] = mode;
                return new Tcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rcreate_Roundtrip()
    {
        ushort tag = 1;
        var qid = new Qid(QidType.QTFILE, 1, 300);
        uint iounit = 8192;
        uint size = NinePConstants.HeaderSize + 17;
        RoundTripTest<Rcreate>(size, tag,
            buffer => new Rcreate(buffer),
            msg => { Assert.Equal(qid.Type, msg.Qid.Type); Assert.Equal(qid.Path, msg.Qid.Path); Assert.Equal(iounit, msg.Iounit); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rcreate);
                int offset = NinePConstants.HeaderSize;
                buf.AsSpan().WriteQid(qid, ref offset);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), iounit);
                return new Rcreate(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tread_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        ulong fileOffset = 1024;
        uint count = 4096;
        uint size = NinePConstants.HeaderSize + 16;
        RoundTripTest<Tread>(size, tag,
            buffer => new Tread(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(fileOffset, msg.Offset); Assert.Equal(count, msg.Count); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Tread);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), fileOffset); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count);
                return new Tread(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rread_Roundtrip()
    {
        ushort tag = 1;
        byte[] dataContent = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        uint count = (uint)dataContent.Length;
        uint size = NinePConstants.HeaderSize + 4 + count;
        
        RoundTripTest<Rread>(size, tag,
            buffer => new Rread(new ReadOnlyMemory<byte>(buffer.ToArray())),
            msg => { Assert.Equal(count, msg.Count); Assert.True(msg.Data.Span.SequenceEqual(dataContent)); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rread);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count); offset += 4;
                dataContent.CopyTo(buf, offset);
                return new Rread(new ReadOnlyMemory<byte>(buf));
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Twrite_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        ulong fileOffset = 1024;
        byte[] dataContent = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        uint count = (uint)dataContent.Length;
        uint size = NinePConstants.HeaderSize + 16 + count;

        RoundTripTest<Twrite>(size, tag,
            buffer => new Twrite(new ReadOnlyMemory<byte>(buffer.ToArray())),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(fileOffset, msg.Offset); Assert.Equal(count, msg.Count); Assert.True(msg.Data.Span.SequenceEqual(dataContent)); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Twrite);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan().Slice(offset, 8), fileOffset); offset += 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count); offset += 4;
                dataContent.CopyTo(buf, offset);
                return new Twrite(new ReadOnlyMemory<byte>(buf));
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rwrite_Roundtrip()
    {
        ushort tag = 1;
        uint count = 4096;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Rwrite>(size, tag,
            buffer => new Rwrite(buffer),
            msg => { Assert.Equal(count, msg.Count); },
            () => {
                var buf = new byte[size];
                buf.WriteHeaders(size, tag, MessageTypes.Rwrite);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), count);
                return new Rwrite(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }
}

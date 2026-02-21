using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class NamespaceTests : TestBase
{
    [Fact]
    public void Test_Twalk_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint newFid = 3;
        string[] wnames = new[] { "a", "b" };
        uint size = NinePConstants.HeaderSize + 4 + 4 + 2 + (uint)(2 + 1) + (uint)(2 + 1);
        RoundTripTest<Twalk>(size, tag,
            buffer => new Twalk(buffer),
            msg => {
                Assert.Equal(fid, msg.Fid);
                Assert.Equal(newFid, msg.NewFid);
                Assert.Equal(wnames.Length, msg.Wname.Length);
                Assert.Equal(wnames[0], msg.Wname[0]);
                Assert.Equal(wnames[1], msg.Wname[1]);
            },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Twalk);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), newFid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), (ushort)wnames.Length); offset += 2;
                buf.AsSpan().WriteString(wnames[0], ref offset);
                buf.AsSpan().WriteString(wnames[1], ref offset);
                return new Twalk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rwalk_Roundtrip()
    {
        ushort tag = 1;
        var qids = new Qid[] { new Qid((QidType)1, 0, 100), new Qid((QidType)1, 0, 101) };
        uint size = NinePConstants.HeaderSize + 2 + (uint)(qids.Length * 13);
        RoundTripTest<Rwalk>(size, tag,
            buffer => new Rwalk(buffer),
            msg => {
                Assert.Equal(qids.Length, msg.Wqid.Length);
                Assert.Equal(qids[0].Path, msg.Wqid[0].Path);
                Assert.Equal(qids[1].Path, msg.Wqid[1].Path);
            },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rwalk);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), (ushort)qids.Length); offset += 2;
                buf.AsSpan().WriteQid(qids[0], ref offset);
                buf.AsSpan().WriteQid(qids[1], ref offset);
                return new Rwalk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tclunk_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Tclunk>(size, tag,
            buffer => new Tclunk(buffer),
            msg => { Assert.Equal(fid, msg.Fid); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Tclunk);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
                return new Tclunk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rclunk_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rclunk>(size, tag,
            buffer => new Rclunk(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rclunk);
                return new Rclunk(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Tremove_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Tremove>(size, tag,
            buffer => new Tremove(buffer),
            msg => { Assert.Equal(fid, msg.Fid); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Tremove);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
                return new Tremove(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rremove_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rremove>(size, tag,
            buffer => new Rremove(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rremove);
                return new Rremove(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }
}

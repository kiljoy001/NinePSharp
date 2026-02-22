using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class MetadataTests : TestBase
{
    [Fact]
    public void Test_Tstat_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        uint size = NinePConstants.HeaderSize + 4;
        RoundTripTest<Tstat>(size, tag,
            buffer => new Tstat(buffer),
            msg => { Assert.Equal(fid, msg.Fid); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Tstat);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
                return new Tstat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rstat_Roundtrip()
    {
        ushort tag = 1;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 100), 0777, 0, 0, 0, "name", "uid", "gid", "muid");
        uint size = NinePConstants.HeaderSize + 2 + (uint)stat.Size;
        RoundTripTest<Rstat>(size, tag,
            buffer => new Rstat(buffer),
            msg => { Assert.Equal(stat.Name, msg.Stat.Name); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rstat);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), stat.Size);
                offset += 2;
                stat.WriteTo(buf.AsSpan(), ref offset);
                return new Rstat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Twstat_Roundtrip()
    {
        ushort tag = 1;
        uint fid = 2;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 100), 0777, 0, 0, 0, "name", "uid", "gid", "muid");
        uint size = NinePConstants.HeaderSize + 4 + 2 + (uint)stat.Size;
        RoundTripTest<Twstat>(size, tag,
            buffer => new Twstat(buffer),
            msg => { Assert.Equal(fid, msg.Fid); Assert.Equal(stat.Name, msg.Stat.Name); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Twstat);
                int offset = NinePConstants.HeaderSize;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid); offset += 4;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), stat.Size); offset += 2;
                stat.WriteTo(buf.AsSpan(), ref offset);
                return new Twstat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }

    [Fact]
    public void Test_Rwstat_Roundtrip()
    {
        ushort tag = 1;
        uint size = NinePConstants.HeaderSize;
        RoundTripTest<Rwstat>(size, tag,
            buffer => new Rwstat(buffer),
            msg => { Assert.Equal(tag, msg.Tag); },
            () => {
                var buf = new byte[size];
                buf.AsSpan().WriteHeaders(size, tag, MessageTypes.Rwstat);
                return new Rwstat(buf);
            },
            (msg, span) => msg.WriteTo(span));
    }
}

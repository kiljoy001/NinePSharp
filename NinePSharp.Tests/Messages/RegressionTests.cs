using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class RegressionTests : TestBase
{
    /// <summary>
    /// Regression test for the Stat framing bug where Rstat/Twstat would double-write the Stat size field.
    /// Standard 9P2000 Rstat: size[4] Rstat tag[2] stat[n]
    /// where stat[n] starts with its own size[2].
    /// </summary>
    [Fact]
    public void Test_Rstat_Framing_Regression()
    {
        ushort tag = 100;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "test", "uid", "gid", "muid");
        
        // Rstat size = Header(7) + stat.Size
        uint expectedSize = 7 + (uint)stat.Size;
        var buf = new byte[expectedSize];
        int offset = 0;
        buf.AsSpan().WriteHeaders(expectedSize, tag, MessageTypes.Rstat);
        offset = 7;
        stat.WriteTo(buf.AsSpan(), ref offset);

        // Byte index 7 and 8 should be the Stat.Size (little-endian ushort)
        ushort writtenStatSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(7, 2));
        Assert.Equal((ushort)(stat.Size - 2), writtenStatSize);
        
        // Ensure no extra padding or double-size write happened.
        // Total bytes written should exactly match expectedSize.
        Assert.Equal((int)expectedSize, offset);

        // Deserialization check
        var rstat = new Rstat(buf);
        Assert.Equal(stat.Name, rstat.Stat.Name);
        Assert.Equal(stat.Size, rstat.Stat.Size);
    }

    /// <summary>
    /// Regression test for the Stat framing bug in Twstat.
    /// Standard 9P2000 Twstat: size[4] Twstat tag[2] fid[4] stat[n]
    /// </summary>
    [Fact]
    public void Test_Twstat_Framing_Regression()
    {
        ushort tag = 101;
        uint fid = 50;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "test", "uid", "gid", "muid");
        
        // Twstat size = Header(7) + fid(4) + stat.Size
        uint expectedSize = 7 + 4 + (uint)stat.Size;
        var buf = new byte[expectedSize];
        int offset = 0;
        buf.AsSpan().WriteHeaders(expectedSize, tag, MessageTypes.Twstat);
        offset = 7;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
        offset += 4;
        stat.WriteTo(buf.AsSpan(), ref offset);

        // Byte index 11 and 12 should be the Stat.Size
        ushort writtenStatSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(11, 2));
        Assert.Equal((ushort)(stat.Size - 2), writtenStatSize);
        
        Assert.Equal((int)expectedSize, offset);

        // Deserialization check
        var twstat = new Twstat(buf);
        Assert.Equal(fid, twstat.Fid);
        Assert.Equal(stat.Name, twstat.Stat.Name);
    }
}

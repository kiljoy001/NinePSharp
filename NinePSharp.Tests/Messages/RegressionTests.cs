using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Messages;

public class RegressionTests : TestBase
{
    /// <summary>
    /// Regression test for Rstat framing:
    /// size[4] Rstat tag[2] nstat[2] stat[nstat]
    /// where stat[nstat] itself starts with stat.size[2].
    /// </summary>
    [Fact]
    public void Test_Rstat_Framing_Regression()
    {
        ushort tag = 100;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "test", "uid", "gid", "muid");
        
        // Rstat size = Header(7) + nstat(2) + stat.Size
        uint expectedSize = 7 + 2 + (uint)stat.Size;
        var buf = new byte[expectedSize];
        int offset = 0;
        buf.AsSpan().WriteHeaders(expectedSize, tag, MessageTypes.Rstat);
        offset = 7;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), stat.Size);
        offset += 2;
        stat.WriteTo(buf.AsSpan(), ref offset);

        // Byte index 7..8 is nstat and 9..10 is stat.size (little-endian ushort).
        ushort nstat = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(7, 2));
        ushort writtenStatSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(9, 2));
        Assert.Equal(stat.Size, nstat);
        Assert.Equal((ushort)(stat.Size - 2), writtenStatSize);
        
        // Total bytes written should exactly match expectedSize.
        Assert.Equal((int)expectedSize, offset);

        // Deserialization check
        var rstat = new Rstat(buf);
        Assert.Equal(stat.Name, rstat.Stat.Name);
        Assert.Equal(stat.Size, rstat.Stat.Size);
        Assert.Equal(stat.Size, rstat.NStat);
    }

    /// <summary>
    /// Regression test for Twstat framing:
    /// size[4] Twstat tag[2] fid[4] nstat[2] stat[nstat]
    /// </summary>
    [Fact]
    public void Test_Twstat_Framing_Regression()
    {
        ushort tag = 101;
        uint fid = 50;
        var stat = new Stat(0, 1, 0, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "test", "uid", "gid", "muid");
        
        // Twstat size = Header(7) + fid(4) + nstat(2) + stat.Size
        uint expectedSize = 7 + 4 + 2 + (uint)stat.Size;
        var buf = new byte[expectedSize];
        int offset = 0;
        buf.AsSpan().WriteHeaders(expectedSize, tag, MessageTypes.Twstat);
        offset = 7;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan().Slice(offset, 4), fid);
        offset += 4;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan().Slice(offset, 2), stat.Size);
        offset += 2;
        stat.WriteTo(buf.AsSpan(), ref offset);

        // Byte index 11..12 is nstat, 13..14 is stat.size.
        ushort nstat = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(11, 2));
        ushort writtenStatSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan().Slice(13, 2));
        Assert.Equal(stat.Size, nstat);
        Assert.Equal((ushort)(stat.Size - 2), writtenStatSize);
        
        Assert.Equal((int)expectedSize, offset);

        // Deserialization check
        var twstat = new Twstat(buf);
        Assert.Equal(fid, twstat.Fid);
        Assert.Equal(stat.Name, twstat.Stat.Name);
        Assert.Equal(stat.Size, twstat.NStat);
    }
}

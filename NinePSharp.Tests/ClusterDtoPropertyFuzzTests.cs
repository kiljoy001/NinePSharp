using NinePSharp.Constants;
using System;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster.Messages;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterDtoPropertyFuzzTests
{
    [Property(MaxTest = 40)]
    public void Request_DTO_Constructors_Preserve_Core_Fields(
        ushort tag,
        uint fid,
        uint newFid,
        ulong offset,
        PositiveInt countSeed,
        byte mode,
        uint validMask)
    {
        uint count = (uint)(countSeed.Get % 8192 + 1);
        var walkNames = new[] { "a", "b", "c" };
        var writeData = Enumerable.Range(0, (int)Math.Min(count, 64)).Select(i => (byte)i).ToArray();

        var walkDto = new TWalkDto(new Twalk(tag, fid, newFid, walkNames));
        walkDto.Tag.Should().Be(tag);
        walkDto.Fid.Should().Be(fid);
        walkDto.NewFid.Should().Be(newFid);
        walkDto.Wname.Should().Equal(walkNames);

        var readDto = new TReadDto(new Tread(tag, fid, offset, count));
        readDto.Tag.Should().Be(tag);
        readDto.Fid.Should().Be(fid);
        readDto.Offset.Should().Be(offset);
        readDto.Count.Should().Be(count);

        var writeDto = new TWriteDto(new Twrite(tag, fid, offset, writeData));
        writeDto.Tag.Should().Be(tag);
        writeDto.Fid.Should().Be(fid);
        writeDto.Offset.Should().Be(offset);
        writeDto.Data.Should().Equal(writeData);

        // Ensure payload is cloned (important for mutation around constructor body removal).
        if (writeData.Length > 0)
        {
            writeData[0] ^= 0xFF;
            writeDto.Data[0].Should().NotBe(writeData[0]);
        }

        var openDto = new TOpenDto(new Topen(tag, fid, mode));
        openDto.Tag.Should().Be(tag);
        openDto.Fid.Should().Be(fid);
        openDto.Mode.Should().Be(mode);

        var clunkDto = new TClunkDto(new Tclunk(tag, fid));
        clunkDto.Tag.Should().Be(tag);
        clunkDto.Fid.Should().Be(fid);

        var statDto = new TStatDto(new Tstat(tag, fid));
        statDto.Tag.Should().Be(tag);
        statDto.Fid.Should().Be(fid);

        var getattrDto = new TGetAttrDto(new Tgetattr(tag, fid, (ulong)validMask));
        getattrDto.Tag.Should().Be(tag);
        getattrDto.Fid.Should().Be(fid);
        getattrDto.RequestMask.Should().Be((ulong)validMask);

        var setattr = new Tsetattr(0, tag, fid, validMask, 0x1A4u, 1000u, 1000u, 4096, 1, 2, 3, 4);
        var setattrDto = new TSetAttrDto(setattr);
        setattrDto.Tag.Should().Be(tag);
        setattrDto.Fid.Should().Be(fid);
        setattrDto.Valid.Should().Be(validMask);
        setattrDto.Mode.Should().Be(0x1A4u);
        setattrDto.FileSize.Should().Be(4096);

        var removeDto = new TRemoveDto(new Tremove(tag, fid));
        removeDto.Tag.Should().Be(tag);
        removeDto.Fid.Should().Be(fid);
    }

    [Property(MaxTest = 40)]
    public void Response_DTO_Constructors_Preserve_Values_And_Serialize_Stat(
        ushort tag,
        uint count,
        NinePDialect dialect)
    {
        var qid = new Qid(QidType.QTFILE, 7, 99);
        bool is9u = dialect == NinePDialect.NineP2000U || dialect == NinePDialect.NineP2000L;
        var stat = new Stat(0, 1, 2, qid, 0644, 3, 4, 5, "n", "u", "g", "m", dialect: dialect, extension: is9u ? "ext" : null);

        var walkDto = new RWalkDto(new Rwalk(tag, new[] { qid }));
        walkDto.Tag.Should().Be(tag);
        walkDto.Wqid.Should().HaveCount(1);
        walkDto.Wqid[0].Should().Be(qid);

        var payload = Enumerable.Range(0, (int)Math.Min(count, 128)).Select(i => (byte)(i % 251)).ToArray();
        var readDto = new RReadDto(new Rread(tag, payload));
        readDto.Tag.Should().Be(tag);
        readDto.Data.Should().Equal(payload);

        var writeDto = new RWriteDto(new Rwrite(tag, count));
        writeDto.Tag.Should().Be(tag);
        writeDto.Count.Should().Be(count);

        var openDto = new ROpenDto(new Ropen(tag, qid, 8192));
        openDto.Tag.Should().Be(tag);
        openDto.Qid.Should().Be(qid);
        openDto.Iounit.Should().Be(8192u);

        new RClunkDto(new Rclunk(tag)).Tag.Should().Be(tag);
        new RWstatDto(new Rwstat(tag)).Tag.Should().Be(tag);
        new RSetAttrDto(new Rsetattr(tag)).Tag.Should().Be(tag);
        new RRemoveDto(new Rremove(tag)).Tag.Should().Be(tag);

        var statDto = new RStatDto(new Rstat(tag, stat));
        statDto.Tag.Should().Be(tag);
        statDto.Dialect.Should().Be(dialect);
        statDto.StatBytes.Length.Should().Be(stat.Size);
        int offset = 0;
        var parsedStat = new Stat(statDto.StatBytes, ref offset, statDto.Dialect);
        parsedStat.Name.Should().Be("n");
        parsedStat.Mode.Should().Be(0644u);

        var getattr = new Rgetattr(tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, 0644, 2000, 3000, 2, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22);
        var getattrDto = new RGetAttrDto(getattr);
        getattrDto.Tag.Should().Be(tag);
        getattrDto.Valid.Should().Be(getattr.Valid);
        getattrDto.Qid.Should().Be(qid);
        getattrDto.Mode.Should().Be(0644u);
        getattrDto.Uid.Should().Be(2000u);
        getattrDto.Gid.Should().Be(3000u);
        getattrDto.DataVersion.Should().Be(22uL);

        var errorDto = new RErrorDto(new Rerror(tag, "boom"));
        errorDto.Tag.Should().Be(tag);
        errorDto.Ename.Should().Be("boom");
    }

    [Property(MaxTest = 25)]
    public void TWstatDto_RoundTrips_Stat_Bytes(string name, uint mode)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "x" : new string(name.Where(char.IsLetterOrDigit).Take(20).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "x";

        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), mode, 0, 0, 0, safeName, "u", "g", "m");
        var tw = new Twstat(1, 2, stat);
        var dto = new TWstatDto(tw);

        dto.Tag.Should().Be(1);
        dto.Fid.Should().Be(2u);
        dto.Dialect.Should().Be(stat.Dialect);
        dto.StatBytes.Length.Should().Be(stat.Size);

        int offset = 0;
        var parsed = new Stat(dto.StatBytes, ref offset, dto.Dialect);
        parsed.Name.Should().Be(safeName);
        parsed.Mode.Should().Be(mode);
    }

    [Fact]
    public void ClusterMessage_Constructors_Preserve_References_And_Ids()
    {
        var message = new object();
        var req = new Remote9PRequest(12345, message);
        var res = new Remote9PResponse(12345, message);

        req.RequestId.Should().Be(12345);
        req.Message.Should().BeSameAs(message);
        res.RequestId.Should().Be(12345);
        res.Message.Should().BeSameAs(message);
    }
}

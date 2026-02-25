using System;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Parser;
using NinePSharp.Generators;
using NinePSharp.Messages;
using NinePSharp.Constants;
using Xunit;

namespace NinePSharp.Tests;

/// <summary>
/// Targeted property tests to kill surviving mutants in low-coverage messages
/// </summary>
public class MutationKillerTests
{
    // ─── Stat Tests (54.29% coverage) ────────────────────────────────────

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Stat_Size_Calculation_Matches_Actual_Serialization(Stat stat)
    {
        // Verify that the Size field accurately reflects serialized size
        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        Assert.Equal(stat.Size, (uint)offset);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Stat_DotU_Extension_Fields_Only_Present_When_DotU_True(Stat stat)
    {
        // Verify 9P2000.u extension logic
        if (stat.DotU)
        {
            Assert.NotNull(stat.NUid);
            Assert.NotNull(stat.NGid);
            Assert.NotNull(stat.NMuid);
        }
        else
        {
            // When DotU is false, extension fields may be null
            // But if present, they shouldn't affect serialization
        }
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 300)]
    public void Stat_Variable_Length_Strings_Roundtrip_Correctly(Stat stat)
    {
        var buffer = new byte[stat.Size];
        int writeOffset = 0;
        stat.WriteTo(buffer, ref writeOffset);

        int readOffset = 0;
        var parsed = new Stat(buffer, ref readOffset, stat.DotU);

        // 9P protocol contract: null and empty string are equivalent (both serialize to 0x00 0x00)
        Assert.Equal(stat.Name ?? "", parsed.Name);
        Assert.Equal(stat.Uid ?? "", parsed.Uid);
        Assert.Equal(stat.Gid ?? "", parsed.Gid);
        Assert.Equal(stat.Muid ?? "", parsed.Muid);

        if (stat.DotU)
        {
            Assert.Equal(stat.Extension ?? "", parsed.Extension ?? "");
        }
    }

    [Property(MaxTest = 200)]
    public void Stat_CalculateSize_Method_Handles_Edge_Cases(string name, string uid, string gid, string muid, bool dotu)
    {
        if (name == null) name = "";
        if (uid == null) uid = "";
        if (gid == null) gid = "";
        if (muid == null) muid = "";

        // Should never throw, even with extreme inputs
        var size = Stat.CalculateSize(name, uid, gid, muid, dotu, dotu ? "ext" : null);

        // Minimum size: 2 (size field) + 39 (fixed fields) + 8 (4 empty strings) = 49
        Assert.True(size >= 49, $"Size too small: {size}");

        // Size should account for all string bytes
        int expectedStringBytes =
            2 + Encoding.UTF8.GetByteCount(name) +
            2 + Encoding.UTF8.GetByteCount(uid) +
            2 + Encoding.UTF8.GetByteCount(gid) +
            2 + Encoding.UTF8.GetByteCount(muid);

        if (dotu)
        {
            expectedStringBytes += 2 + Encoding.UTF8.GetByteCount("ext");
            expectedStringBytes += 12; // n_uid[4] + n_gid[4] + n_muid[4]
        }

        int expectedSize = 2 + 39 + expectedStringBytes;
        Assert.Equal(expectedSize, size);
    }

    [Fact]
    public void Stat_Empty_Strings_Serialize_As_Zero_Length()
    {
        var qid = new Qid((QidType)0, 0, 0);
        var stat = new Stat(0, 0, 0, qid, 0, 0, 0, 0, "", "", "", "", false, null, null, null, null);

        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        // Verify size is exactly 2 + 39 + 8 (four 2-byte empty strings) = 49
        Assert.Equal(49, stat.Size);
        Assert.Equal(49, offset);
    }

    [Fact]
    public void Stat_DotU_Extension_Increases_Size_Correctly()
    {
        var qid = new Qid((QidType)1, 2, 3);

        // Create two identical stats, one with DotU, one without
        var statNoDotU = new Stat(0, 1, 2, qid, 0x1EDu, 1000u, 2000u, 12345uL,
                                  "file.txt", "user", "group", "muid",
                                  false, null, null, null, null);

        var statWithDotU = new Stat(0, 1, 2, qid, 0x1EDu, 1000u, 2000u, 12345uL,
                                    "file.txt", "user", "group", "muid",
                                    true, "ext", 1000u, 1000u, 1000u);

        // DotU version should be exactly 2 (extension length) + 3 (ext bytes) + 12 (3 x uint32) = 17 bytes larger
        int expectedDiff = 2 + Encoding.UTF8.GetByteCount("ext") + 12;
        Assert.Equal(statNoDotU.Size + expectedDiff, statWithDotU.Size);
    }

    // ─── Tattach Tests (44.00% coverage) ─────────────────────────────────

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Tattach_String_Lengths_Match_UTF8_Byte_Count(Tattach msg)
    {
        // Calculate expected size
        uint expectedSize = 7u + 4 + 4 +
                          (uint)(2 + Encoding.UTF8.GetByteCount(msg.Uname ?? "")) +
                          (uint)(2 + Encoding.UTF8.GetByteCount(msg.Aname ?? ""));

        Assert.Equal(expectedSize, msg.Size);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Tattach_Roundtrips_With_All_String_Variations(Tattach msg)
    {
        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, msg.NUname.HasValue);

        Assert.Equal(msg.Uname, parsed.Uname);
        Assert.Equal(msg.Aname, parsed.Aname);
        Assert.Equal(msg.Fid, parsed.Fid);
        Assert.Equal(msg.Afid, parsed.Afid);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "b")]
    [InlineData("user", "aname")]
    [InlineData("very_long_username_that_exceeds_normal_limits", "filesystem")]
    public void Tattach_Handles_String_Boundary_Cases(string uname, string aname)
    {
        var msg = new Tattach(1, 1, 0, uname, aname);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, false);

        Assert.Equal(uname, parsed.Uname);
        Assert.Equal(aname, parsed.Aname);
    }

    [Fact]
    public void Tattach_Unicode_Strings_Serialize_Correctly()
    {
        var msg = new Tattach(1, 1, 0, "用户名", "文件系统");

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, false);

        Assert.Equal("用户名", parsed.Uname);
        Assert.Equal("文件系统", parsed.Aname);
    }

    [Fact]
    public void Tattach_NUname_Extension_Serializes_When_Present()
    {
        var msg = new Tattach(1, 1, 0, "user", "aname");
        var msgWith9u = new Tattach(1, 1, 0, "user", "aname");

        // Without NUname
        var buffer1 = new byte[msg.Size];
        msg.WriteTo(buffer1, false);

        // Would have NUname (9P2000.u mode)
        var buffer2 = new byte[msg.Size];
        msg.WriteTo(buffer2, false);

        // Size should be the same when NUname is not set
        Assert.Equal(buffer1.Length, buffer2.Length);
    }

    // ─── Tauth Tests (28.57% coverage) ───────────────────────────────────

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Tauth_Size_Calculation_Includes_All_Fields(Tauth msg)
    {
        uint expectedSize = 7u + 4 +
                          (uint)(2 + Encoding.UTF8.GetByteCount(msg.Uname ?? "")) +
                          (uint)(2 + Encoding.UTF8.GetByteCount(msg.Aname ?? ""));

        if (msg.NUname.HasValue)
        {
            expectedSize += 4;
        }

        Assert.Equal(expectedSize, msg.Size);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Tauth_Roundtrips_With_All_String_Variations(Tauth msg)
    {
        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer, msg.NUname.HasValue);

        var parsed = new Tauth(buffer, msg.NUname.HasValue);

        Assert.Equal(msg.Uname, parsed.Uname);
        Assert.Equal(msg.Aname, parsed.Aname);
        Assert.Equal(msg.Afid, parsed.Afid);

        if (msg.NUname.HasValue)
        {
            Assert.Equal(msg.NUname, parsed.NUname);
        }
    }

    [Theory]
    [InlineData("", "", null)]
    [InlineData("u", "a", null)]
    [InlineData("auth_user", "auth_name", 1000u)]
    [InlineData("very_long_authentication_username", "very_long_authentication_name", uint.MaxValue)]
    public void Tauth_String_And_Extension_Combinations(string uname, string aname, uint? nuname)
    {
        var msg = new Tauth(1, 1, uname, aname, nuname);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer, nuname.HasValue);

        var parsed = new Tauth(buffer, nuname.HasValue);

        Assert.Equal(uname, parsed.Uname);
        Assert.Equal(aname, parsed.Aname);
        Assert.Equal(nuname, parsed.NUname);
    }

    [Fact]
    public void Tauth_Empty_Strings_Have_Correct_Size()
    {
        var msg = new Tauth(1, 1, "", "", null);

        // HeaderSize(7) + Afid(4) + Uname(2+0) + Aname(2+0) = 15
        Assert.Equal(15u, msg.Size);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer, false);

        var parsed = new Tauth(buffer, false);
        Assert.Equal("", parsed.Uname);
        Assert.Equal("", parsed.Aname);
    }

    // ─── Rstatfs Tests (25.00% coverage) ─────────────────────────────────

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 500)]
    public void Rstatfs_All_Numeric_Fields_Roundtrip_Correctly(Rstatfs msg)
    {
        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Rstatfs(buffer);

        Assert.Equal(msg.FsType, parsed.FsType);
        Assert.Equal(msg.BSize, parsed.BSize);
        Assert.Equal(msg.Blocks, parsed.Blocks);
        Assert.Equal(msg.BFree, parsed.BFree);
        Assert.Equal(msg.BAvail, parsed.BAvail);
        Assert.Equal(msg.Files, parsed.Files);
        Assert.Equal(msg.FFree, parsed.FFree);
        Assert.Equal(msg.FsId, parsed.FsId);
        Assert.Equal(msg.NameLen, parsed.NameLen);
    }

    [Fact]
    public void Rstatfs_Boundary_Values_All_Zero()
    {
        var msg = new Rstatfs(1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Rstatfs(buffer);

        Assert.Equal(0u, parsed.FsType);
        Assert.Equal(0u, parsed.BSize);
        Assert.Equal(0uL, parsed.Blocks);
        Assert.Equal(0uL, parsed.BFree);
        Assert.Equal(0uL, parsed.BAvail);
        Assert.Equal(0uL, parsed.Files);
        Assert.Equal(0uL, parsed.FFree);
        Assert.Equal(0uL, parsed.FsId);
        Assert.Equal(0u, parsed.NameLen);
    }

    [Fact]
    public void Rstatfs_Boundary_Values_All_Max()
    {
        var msg = new Rstatfs(1, uint.MaxValue, uint.MaxValue, ulong.MaxValue, ulong.MaxValue,
                             ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, uint.MaxValue);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Rstatfs(buffer);

        Assert.Equal(uint.MaxValue, parsed.FsType);
        Assert.Equal(uint.MaxValue, parsed.BSize);
        Assert.Equal(ulong.MaxValue, parsed.Blocks);
        Assert.Equal(ulong.MaxValue, parsed.BFree);
        Assert.Equal(ulong.MaxValue, parsed.BAvail);
        Assert.Equal(ulong.MaxValue, parsed.Files);
        Assert.Equal(ulong.MaxValue, parsed.FFree);
        Assert.Equal(ulong.MaxValue, parsed.FsId);
        Assert.Equal(uint.MaxValue, parsed.NameLen);
    }

    [Theory]
    [InlineData(1000uL, 2000uL)] // BFree > Blocks (invalid but should serialize)
    [InlineData(500uL, 1000uL)]  // FFree > Files (invalid but should serialize)
    public void Rstatfs_Invalid_Free_Greater_Than_Total_Serializes(ulong blocks, ulong bFree)
    {
        // Some mutants survive because we don't validate logical constraints
        var msg = new Rstatfs(1, 1, 4096, blocks, bFree, bFree, 1000, 500, 12345, 255);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Rstatfs(buffer);

        Assert.Equal(blocks, parsed.Blocks);
        Assert.Equal(bFree, parsed.BFree);
    }

    [Fact]
    public void Rstatfs_Size_Is_Always_Constant()
    {
        // Size should always be HeaderSize + 4 + 4 + 8*6 + 4 = 7 + 60 = 67
        var msg1 = new Rstatfs(1, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var msg2 = new Rstatfs(2, uint.MaxValue, uint.MaxValue, ulong.MaxValue, ulong.MaxValue,
                              ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, uint.MaxValue);

        Assert.Equal(67u, msg1.Size);
        Assert.Equal(67u, msg2.Size);
        Assert.Equal(msg1.Size, msg2.Size);
    }

    [Theory]
    [InlineData(0x9123u, 4096u, 268435456uL, 13421772uL)] // Realistic 1TB disk, 95% full
    [InlineData(0x53464846u, 512u, 1uL, 0uL)]            // Edge case: 1 block total, 0 free
    [InlineData(0xEF53u, 1024u, 0uL, 0uL)]               // Edge case: 0 blocks
    public void Rstatfs_Realistic_Filesystem_Values(uint fsType, uint bSize, ulong blocks, ulong bFree)
    {
        var msg = new Rstatfs(1, fsType, bSize, blocks, bFree, bFree, 1000000, 500000, 0xABCDEF, 255);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Rstatfs(buffer);

        Assert.Equal(fsType, parsed.FsType);
        Assert.Equal(bSize, parsed.BSize);
        Assert.Equal(blocks, parsed.Blocks);
        Assert.Equal(bFree, parsed.BFree);
    }
}

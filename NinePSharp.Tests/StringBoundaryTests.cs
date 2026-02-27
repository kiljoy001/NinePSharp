using NinePSharp.Constants;
using System;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Generators;
using Xunit;

namespace NinePSharp.Tests;

/// <summary>
/// Tests to verify null vs empty string boundary contract
/// These tests kill mutants that survive when null/empty string handling is inconsistent
/// </summary>
public class StringBoundaryTests
{
    // ─── Null vs Empty String Contract ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Stat_Null_And_Empty_Strings_Serialize_Identically(string? testString)
    {
        // Contract: NinePSharp treats null and empty string as equivalent (0-length string in 9P)
        var qid = new Qid((QidType)0, 0, 0);

        var stat = new Stat(0, 0, 0, qid, 0, 0, 0, 0,
                           testString, testString, testString, testString,
                           NinePDialect.NineP2000, null, null, null, null);

        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        // Parse it back
        int readOffset = 0;
        var parsed = new Stat(buffer, ref readOffset, NinePDialect.NineP2000);

        // After roundtrip, both null and "" should become ""
        Assert.Equal("", parsed.Name);
        Assert.Equal("", parsed.Uid);
        Assert.Equal("", parsed.Gid);
        Assert.Equal("", parsed.Muid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Tattach_Null_And_Empty_Strings_Serialize_Identically(string? testString)
    {
        var msg = new Tattach(1, 100, 0xFFFFFFFF, testString, testString);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, false);

        // After roundtrip, both null and "" should become ""
        Assert.Equal("", parsed.Uname);
        Assert.Equal("", parsed.Aname);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Tauth_Null_And_Empty_Strings_Serialize_Identically(string? testString)
    {
        var msg = new Tauth(1, 1, testString, testString, null);

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tauth(buffer, false);

        // After roundtrip, both null and "" should become ""
        Assert.Equal("", parsed.Uname);
        Assert.Equal("", parsed.Aname);
    }

    [Fact]
    public void Stat_Null_String_Size_Calculation()
    {
        // Verify that null strings count as 0-length (2 bytes for length prefix, 0 bytes for content)
        var qid = new Qid((QidType)0, 0, 0);
        var statNull = new Stat(0, 0, 0, qid, 0, 0, 0, 0, null, null, null, null, NinePDialect.NineP2000, null, null, null, null);
        var statEmpty = new Stat(0, 0, 0, qid, 0, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000, null, null, null, null);

        // Both should have identical size
        Assert.Equal(statEmpty.Size, statNull.Size);
        Assert.Equal(49, statNull.Size); // 2 (size) + 39 (fixed) + 8 (4 * 2-byte empty strings) = 49
    }

    [Fact]
    public void Tattach_Null_String_Size_Calculation()
    {
        var msgNull = new Tattach(1, 100, 0xFFFFFFFF, null, null);
        var msgEmpty = new Tattach(1, 100, 0xFFFFFFFF, "", "");

        // Both should have identical size
        Assert.Equal(msgEmpty.Size, msgNull.Size);
        Assert.Equal(19u, msgNull.Size); // 7 (header) + 4 (fid) + 4 (afid) + 2 (uname) + 2 (aname) = 19
    }

    // ─── Whitespace vs Empty ─────────────────────────────────────────────

    [Fact]
    public void Stat_Whitespace_String_Not_Equivalent_To_Empty()
    {
        // Whitespace is NOT the same as empty
        var qid = new Qid((QidType)0, 0, 0);
        var statSpace = new Stat(0, 0, 0, qid, 0, 0, 0, 0, " ", " ", " ", " ", NinePDialect.NineP2000, null, null, null, null);
        var statEmpty = new Stat(0, 0, 0, qid, 0, 0, 0, 0, "", "", "", "", NinePDialect.NineP2000, null, null, null, null);

        // Size should differ (1 byte per space)
        Assert.NotEqual(statEmpty.Size, statSpace.Size);
        Assert.Equal(statEmpty.Size + 4, statSpace.Size); // 4 spaces = 4 extra bytes
    }

    [Fact]
    public void Stat_Whitespace_Roundtrips_Correctly()
    {
        var qid = new Qid((QidType)0, 0, 0);
        var stat = new Stat(0, 0, 0, qid, 0, 0, 0, 0, " ", "\t", "\n", "\r", NinePDialect.NineP2000, null, null, null, null);

        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        int readOffset = 0;
        var parsed = new Stat(buffer, ref readOffset, NinePDialect.NineP2000);

        // Whitespace should be preserved exactly
        Assert.Equal(" ", parsed.Name);
        Assert.Equal("\t", parsed.Uid);
        Assert.Equal("\n", parsed.Gid);
        Assert.Equal("\r", parsed.Muid);
    }

    // ─── UTF8 Encoding Edge Cases ───────────────────────────────────────

    [Theory]
    [InlineData("café")]           // 2-byte UTF8 chars
    [InlineData("日本語")]         // 3-byte UTF8 chars
    [InlineData("𝕳𝖊𝖑𝖑𝖔")]          // 4-byte UTF8 chars
    [InlineData("💩")]             // Emoji (4-byte UTF8)
    public void Stat_Unicode_Strings_Roundtrip_With_Correct_Byte_Count(string unicode)
    {
        var qid = new Qid((QidType)0, 0, 0);
        var stat = new Stat(0, 0, 0, qid, 0, 0, 0, 0, unicode, unicode, unicode, unicode, NinePDialect.NineP2000, null, null, null, null);

        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        // Verify size accounts for UTF8 bytes, not character count
        int expectedUtf8Bytes = Encoding.UTF8.GetByteCount(unicode);
        int expectedSize = 2 + 39 + (4 * (2 + expectedUtf8Bytes)); // size + fixed + (4 strings * (len + bytes))

        Assert.Equal(expectedSize, stat.Size);

        // Roundtrip
        int readOffset = 0;
        var parsed = new Stat(buffer, ref readOffset, NinePDialect.NineP2000);

        Assert.Equal(unicode, parsed.Name);
        Assert.Equal(unicode, parsed.Uid);
        Assert.Equal(unicode, parsed.Gid);
        Assert.Equal(unicode, parsed.Muid);
    }

    [Fact]
    public void Tattach_Emoji_In_Username_Roundtrips()
    {
        var msg = new Tattach(1, 100, 0xFFFFFFFF, "user💩", "fs🚀");

        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, false);

        Assert.Equal("user💩", parsed.Uname);
        Assert.Equal("fs🚀", parsed.Aname);
    }

    // ─── Property-Based String Boundary Tests ───────────────────────────

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 200)]
    public void Stat_Null_Vs_Empty_Roundtrip(Stat stat)
    {
        // Regardless of input (null or empty), after roundtrip all strings should be non-null
        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        int readOffset = 0;
        var parsed = new Stat(buffer, ref readOffset, stat.Dialect);

        // After roundtrip, no strings should be null
        Assert.NotNull(parsed.Name);
        Assert.NotNull(parsed.Uid);
        Assert.NotNull(parsed.Gid);
        Assert.NotNull(parsed.Muid);

        if (stat.Dialect != NinePDialect.NineP2000)
        {
            // Extension can be null if not Dialect, but if Dialect it should roundtrip
            if (stat.Extension != null)
            {
                Assert.NotNull(parsed.Extension);
            }
        }
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 200)]
    public void Tattach_Null_Vs_Empty_Roundtrip(Tattach msg)
    {
        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tattach(buffer, false);

        // After roundtrip, strings should be non-null
        Assert.NotNull(parsed.Uname);
        Assert.NotNull(parsed.Aname);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 200)]
    public void Tauth_Null_Vs_Empty_Roundtrip(Tauth msg)
    {
        var buffer = new byte[msg.Size];
        msg.WriteTo(buffer);

        var parsed = new Tauth(buffer, false);

        // After roundtrip, strings should be non-null
        Assert.NotNull(parsed.Uname);
        Assert.NotNull(parsed.Aname);
    }
}

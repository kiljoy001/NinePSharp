using System;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.FileSystem;
using Xunit;

namespace NinePSharp.Tests;

// 9P2000.u's additions: a stat's extension and numeric ids, an error's errno and an auth's n_uname,
// read and written only in that dialect.
public class UnixExtensionTests
{
    private static readonly Qid FileQid = new(QidType.QTFILE, 3, 12345);

    [Fact]
    public void A_9P2000_stat_has_no_numeric_ids_and_a_unix_one_defaults_them_to_none()
    {
        Assert.Equal((null, null, null), Ids(NewStat(NinePDialect.NineP2000)));
        Assert.Equal((uint.MaxValue, uint.MaxValue, uint.MaxValue), Ids(NewStat(NinePDialect.NineP2000U)));
        Assert.Equal((21u, 22u, 23u), Ids(NewStat(NinePDialect.NineP2000U, "link", 21, 22, 23)));
    }

    [Fact]
    public void A_stat_given_its_size_keeps_it_and_one_given_none_is_sized_from_its_fields()
    {
        Assert.Equal(70, new Stat(70, 0, 0, FileQid, 0, 0, 0, 0, "n", "u", "g", "m").Size);
        Assert.Equal(2 + 39 + (2 + 1) + (2 + 3) + (2 + 3) + (2 + 4), NewStat(NinePDialect.NineP2000, name: "n").Size);
        Assert.Equal(2 + 39 + (2 + 1) + (2 + 3) + (2 + 3) + (2 + 4) + (2 + 4) + 12, NewStat(NinePDialect.NineP2000U, "link", name: "n").Size);
    }

    [Fact]
    public void A_unix_stat_is_read_back_with_every_field_from_anywhere_in_a_buffer()
    {
        Stat stat = NewStat(NinePDialect.NineP2000U, "link", 21, 22, 23);
        var buffer = new byte[20 + stat.Size];
        int offset = 20;
        stat.WriteTo(buffer, ref offset);

        int read = 20;
        var parsed = new Stat(buffer, ref read, NinePDialect.NineP2000U);

        Assert.Equal(buffer.Length, offset);
        Assert.Equal(buffer.Length, read);
        Assert.Equal(Describe(stat), Describe(parsed));
    }

    [Fact]
    public void A_stat_without_the_unix_fields_read_as_unix_has_none()
    {
        Stat stat = NewStat(NinePDialect.NineP2000);
        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        int read = 0;
        var parsed = new Stat(buffer, ref read, NinePDialect.NineP2000U);

        Assert.Equal((null, null, null, null), (parsed.Extension, parsed.NUid, parsed.NGid, parsed.NMuid));
    }

    [Fact]
    public void A_unix_stat_read_as_9P2000_ignores_the_unix_fields()
    {
        Stat stat = NewStat(NinePDialect.NineP2000U, "link", 21, 22, 23);
        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        int read = 0;
        var parsed = new Stat(buffer, ref read, NinePDialect.NineP2000);

        Assert.Equal((null, null, null, null), (parsed.Extension, parsed.NUid, parsed.NGid, parsed.NMuid));
        Assert.Equal("name", parsed.Name);
    }

    [Fact]
    public void A_stat_whose_size_does_not_match_its_fields_is_not_written()
    {
        var stat = new Stat(70, 0, 0, FileQid, 0, 0, 0, 0, "n", "u", "g", "m");
        var buffer = new byte[100];
        int offset = 0;

        Assert.Throws<InvalidOperationException>(() => stat.WriteTo(buffer, ref offset));
        Assert.Equal(0, offset);
    }

    [Fact]
    public void A_hardlink_stats_under_its_own_name_and_keeps_the_unix_fields()
    {
        var link = new NinePHardlink("a-much-longer-name", new NinePFile("x", Encoding.UTF8.GetBytes("hello")));

        foreach (NinePDialect dialect in new[] { NinePDialect.NineP2000, NinePDialect.NineP2000U })
        {
            Stat stat = link.GetStat(dialect);
            var buffer = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(buffer, ref offset);

            int read = 0;
            var parsed = new Stat(buffer, ref read, dialect);
            Assert.Equal(("a-much-longer-name", buffer.Length), (parsed.Name, read));
            Assert.Equal(Ids(new NinePFile("x", []).GetStat(dialect)), Ids(parsed));
        }
    }

    [Fact]
    public void An_error_with_an_errno_is_sized_and_written_with_it_only_in_the_unix_dialect()
    {
        var error = new Rerror(1, "gone", 5);
        var unix = new byte[error.Size];
        error.WriteTo(unix, is9u: true);
        byte[] plain = Filled(error.Size);
        error.WriteTo(plain, is9u: false);

        Assert.Equal(7 + 2 + 4 + 4, (int)error.Size);
        Assert.Equal((uint?)5, new Rerror(unix, is9u: true).Ecode);
        Assert.Null(new Rerror(unix, is9u: false).Ecode);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, plain[13..]);
    }

    [Fact]
    public void An_error_without_an_errno_read_as_unix_has_none()
    {
        var error = new Rerror(1, "gone");
        var buffer = new byte[error.Size];
        error.WriteTo(buffer, is9u: true);

        Assert.Equal(7 + 2 + 4, (int)new Rerror(1, "gone", null).Size);
        Assert.Null(new Rerror(buffer, is9u: true).Ecode);
    }

    [Fact]
    public void An_auth_with_a_numeric_user_is_sized_and_written_with_it_only_in_the_unix_dialect()
    {
        var auth = new Tauth(1, 2, "scott", "main", 1000);
        var unix = new byte[auth.Size];
        auth.WriteTo(unix, is9u: true);
        byte[] plain = Filled(auth.Size);
        auth.WriteTo(plain, is9u: false);

        Assert.Equal(7 + 4 + (2 + 5) + (2 + 4) + 4, (int)auth.Size);
        Assert.Equal(("scott", "main", (uint?)1000), Describe(new Tauth(unix, is9u: true)));
        Assert.Equal(("scott", "main", (uint?)null), Describe(new Tauth(unix, is9u: false)));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, plain[24..]);
    }

    [Fact]
    public void An_auth_without_names_or_a_numeric_user_has_empty_names_and_none()
    {
        var auth = new Tauth(1, 2, null, null);
        var buffer = new byte[auth.Size];
        auth.WriteTo(buffer, is9u: true);

        Assert.Equal(7 + 4 + 2 + 2, (int)auth.Size);
        Assert.Equal((string.Empty, string.Empty, (uint?)null), Describe(new Tauth(buffer, is9u: true)));
    }

    private static Stat NewStat(NinePDialect dialect, string? extension = null, uint? nUid = null, uint? nGid = null, uint? nMuid = null, string name = "name")
        => new(0, 0x4D, 7, FileQid, 0b110_100_100, 11, 13, 17, name, "uid", "gid", "muid", dialect, extension, nUid, nGid, nMuid);

    private static (uint? Uid, uint? Gid, uint? Muid) Ids(Stat stat) => (stat.NUid, stat.NGid, stat.NMuid);

    private static string Describe(Stat stat)
        => $"{stat.Size} {stat.Type} {stat.Dev} {stat.Qid.Type} {stat.Qid.Version} {stat.Qid.Path} {stat.Mode} {stat.Atime} {stat.Mtime} {stat.Length} "
            + $"{stat.Name} {stat.Uid} {stat.Gid} {stat.Muid} {stat.Extension} {stat.NUid} {stat.NGid} {stat.NMuid}";

    private static (string Uname, string Aname, uint? NUname) Describe(Tauth auth) => (auth.Uname, auth.Aname, auth.NUname);

    private static byte[] Filled(uint size)
    {
        var buffer = new byte[size];
        Array.Fill(buffer, (byte)0xFF);
        return buffer;
    }
}

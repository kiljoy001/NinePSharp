using NinePSharp.Constants;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Tests;

// 9P strings are Plan 9's UTF-8: counted in bytes, written as runetochar writes each rune, and read
// as chartorune reads them, a surrogate being a rune like any other and each bad byte a Runeerror.
public class Plan9StringTests
{
    [Fact]
    public void A_walk_is_sized_by_the_bytes_of_its_names()
    {
        var walk = new Twalk(1, 2, 3, ["café", "日本語", "💩"]);
        var buffer = new byte[walk.Size];
        walk.WriteTo(buffer);

        Assert.Equal(17 + (2 + 5) + (2 + 9) + (2 + 4), (int)walk.Size);
        Assert.Equal(["café", "日本語", "💩"], new Twalk(buffer).Wname);
    }

    [Fact]
    public void An_unpaired_surrogate_is_written_and_read_as_the_rune_it_is()
    {
        var walk = new Twalk(1, 2, 3, ["\uD800"]);
        var buffer = new byte[walk.Size];
        walk.WriteTo(buffer);

        Assert.Equal(new byte[] { 3, 0, 0xED, 0xA0, 0x80 }, buffer[17..]);
        Assert.Equal(["\uD800"], new Twalk(buffer).Wname);
    }

    [Fact]
    public void Each_byte_of_a_cut_short_sequence_is_read_as_a_runeerror()
    {
        byte[] message = [11, 0, 0, 0, (byte)MessageTypes.Rerror, 1, 0, 2, 0, 0xE2, 0x82];

        Assert.Equal("��", new Rerror(message).Ename);
    }

    [Fact]
    public void A_created_name_with_a_surrogate_is_sized_by_its_bytes()
    {
        var create = new Tcreate(1, 2, "a\uDC00", 0b110_110_110, NinePConstants.OWRITE);
        var buffer = new byte[create.Size];
        create.WriteTo(buffer);

        Assert.Equal(7 + 4 + 2 + 4 + 4 + 1, (int)create.Size);
        Assert.Equal("a\uDC00", new Tcreate(buffer).Name);
    }

    [Fact]
    public void A_stat_with_a_surrogate_is_sized_by_its_bytes()
    {
        var stat = new Stat(0, 0, 0, new Qid(0, 0, 0), 0, 0, 0, 0, "\uD83D", "glenda", "glenda", "glenda");
        var buffer = new byte[stat.Size];
        int offset = 0;
        stat.WriteTo(buffer, ref offset);

        Assert.Equal(2 + 39 + (2 + 3) + (3 * (2 + 6)), (int)stat.Size);
        offset = 0;
        Assert.Equal("\uD83D", new Stat(buffer, ref offset).Name);
    }

    [Fact]
    public void An_error_with_a_surrogate_is_sized_by_its_bytes()
    {
        var error = new Rerror(1, "\uD800!");
        var buffer = new byte[error.Size];
        error.WriteTo(buffer);

        Assert.Equal(7 + 2 + 4, (int)error.Size);
        Assert.Equal("\uD800!", new Rerror(buffer).Ename);
    }
}

using System.Buffers.Binary;
using System.Text;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using Xunit;

namespace NinePSharp.Tests;

public sealed class ExtendedAttachTests
{
    [Property(MaxTest = 100)]
    public bool NumericUserRoundTrips(ushort tag, uint fid, uint afid, uint user)
    {
        var request = new Tattach(tag, fid, afid, "玩家", "/", user);
        byte[] bytes = new byte[request.Size];
        request.WriteTo(bytes);
        var parsed = NinePParser.parse(NinePDialect.NineP2000L, bytes.AsMemory());
        var attach = Assert.IsType<NinePMessage.MsgTattach>(parsed.ResultValue).Item;
        Assert.Equal(19 + Encoding.UTF8.GetByteCount("玩家") + 1 + 4, bytes.Length);
        return attach.Tag == tag && attach.Fid == fid && attach.Afid == afid
            && attach.NUname == user && attach.Uname == "玩家" && attach.Aname == "/"
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4)) == user;
    }

    [Fact]
    public void ClassicAttachRetainsItsOriginalWireShape()
    {
        var request = new Tattach(7, 8, NinePConstants.NoFid, null, null);
        Assert.Null(request.NUname);
        Assert.Equal(19U, request.Size);
        byte[] bytes = new byte[request.Size];
        request.WriteTo(bytes);
        var result = Assert.IsType<NinePMessage.MsgTattach>(NinePParser.parse(NinePDialect.NineP2000, bytes.AsMemory()).ResultValue).Item;
        Assert.Equal((ushort)7, result.Tag);
        Assert.Equal(8U, result.Fid);
        Assert.Equal(NinePConstants.NoFid, result.Afid);
        Assert.Equal(string.Empty, result.Uname);
        Assert.Equal(string.Empty, result.Aname);
        Assert.Null(result.NUname);
    }
    [Fact]
    public void NumericExtensionIsReadAndWrittenOnlyWhenRequestedAndPresent()
    {
        var request = new Tattach(1, 2, 3, "user", "/", 42);
        byte[] bytes = new byte[request.Size];
        request.WriteTo(bytes);
        Assert.Null(new Tattach(bytes, false).NUname);
        Assert.Equal(42U, new Tattach(bytes, true).NUname);
        Assert.Null(new Tattach(bytes.AsSpan(0, bytes.Length - 1), true).NUname);
        Array.Fill(bytes, (byte)0xAA);
        request.WriteTo(bytes, false);
        Assert.Equal(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA }, bytes[^4..]);
        var classic = new Tattach(1, 2, 3, "user", "/");
        classic.WriteTo(new byte[classic.Size], true);
    }

}

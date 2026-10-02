using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class LinuxProtocolInvariantTests
{
    [Fact]
    public void OpenFlagTranslationPreservesEveryAccessAndTruncateCombination()
    {
        for (byte access = 0; access < 3; access++)
        {
            foreach (bool truncate in new[] { false, true })
            {
                uint flags = access | (truncate ? 0x200U : 0U);
                byte mode = LinuxProtocol.ToOpenMode(flags);

                Assert.Equal(access, mode & 3);
                Assert.Equal(truncate, (mode & NinePConstants.OTRUNC) != 0);
            }
        }
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(511UL)]
    [InlineData(512UL)]
    [InlineData(513UL)]
    [InlineData(ulong.MaxValue)]
    public void GetAttrBlockCountIsCeilingOfLengthOver512(ulong length)
    {
        var resource = new ResourceHandle(
            new ResourceIdentity("property", "resource", 1),
            QidType.QTFILE,
            3);
        var stat = new ResourceStat(resource, "file", 0x1A4, 1, 2, length, "u", "g", "m");
        var request = new Tgetattr(1, 1, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BLOCKS);

        Rgetattr result = LinuxProtocol.ToGetAttr(request, stat);

        ulong expected = (length / 512) + (length % 512 == 0 ? 0UL : 1UL);
        Assert.Equal(expected, result.Blocks);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData(ushort.MaxValue)]
    public void ReaddirCookiePointsImmediatelyAfterEntry(ushort suffix)
    {
        string name = $"entry-{suffix}";
        var resource = new ResourceHandle(
            new ResourceIdentity("property", "resource", 1),
            QidType.QTFILE);
        var stat = new ResourceStat(resource, name, 0x1A4, 0, 0, 0, "u", "g", "m");

        ReadOnlySpan<byte> encoded = LinuxProtocol.EncodeDirectory(new[] { stat }).Span;

        Assert.Equal((ulong)encoded.Length, BinaryPrimitives.ReadUInt64LittleEndian(encoded.Slice(13, 8)));
        Assert.Equal(8, encoded[21]);
    }

    [Fact]
    public void ReaddirReturnsOnlyWholeEntriesAndResumesAtItsCookie()
    {
        ResourceStat first = CreateStat("first");
        ResourceStat second = CreateStat("second");
        int firstLength = LinuxProtocol.EncodeDirectory(new[] { first }).Length;

        Assert.Equal(0, LinuxProtocol.EncodeDirectory(new[] { first, second }, 0, (uint)(firstLength - 1)).Length);

        ReadOnlyMemory<byte> firstPage = LinuxProtocol.EncodeDirectory(
            new[] { first, second },
            0,
            (uint)firstLength);
        ulong cookie = BinaryPrimitives.ReadUInt64LittleEndian(firstPage.Span.Slice(13, 8));
        ReadOnlyMemory<byte> secondPage = LinuxProtocol.EncodeDirectory(
            new[] { first, second },
            cookie,
            uint.MaxValue);

        Assert.Equal(firstLength, firstPage.Length);
        Assert.Equal("second".Length, BinaryPrimitives.ReadUInt16LittleEndian(secondPage.Span.Slice(22, 2)));
        Assert.Equal(
            (ulong)(firstPage.Length + secondPage.Length),
            BinaryPrimitives.ReadUInt64LittleEndian(secondPage.Span.Slice(13, 8)));
    }

    [Fact]
    public void ReaddirRejectsAnOffsetInsideAnEntryAndEndsPastTheDirectory()
    {
        ResourceStat stat = CreateStat("entry");
        int length = LinuxProtocol.EncodeDirectory(new[] { stat }).Length;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LinuxProtocol.EncodeDirectory(new[] { stat }, 1, uint.MaxValue));
        Assert.Equal(0, LinuxProtocol.EncodeDirectory(new[] { stat }, (ulong)length + 1, uint.MaxValue).Length);
    }

    private static ResourceStat CreateStat(string name)
    {
        var resource = new ResourceHandle(
            new ResourceIdentity("property", name, 1),
            QidType.QTFILE);
        return new ResourceStat(resource, name, 0x1A4, 0, 0, 0, "u", "g", "m");
    }
}

using System.Buffers.Binary;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class FileWStatRecordTests
{
    [Fact]
    public void UnchangedRecordPreservesEveryNativeSentinel()
    {
        byte[] bytes = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged());
        ResourceWStat decoded = FileStatRecords.DecodeUpdate(FileStatRecords.ValidateAndCopyUpdate(bytes));
        Assert.Equal(49, bytes.Length);
        Assert.Equal(47, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal(ushort.MaxValue, decoded.Type);
        Assert.Equal(uint.MaxValue, decoded.Device);
        Assert.Equal(byte.MaxValue, (byte)decoded.Qid.Type);
        Assert.Equal(uint.MaxValue, decoded.Qid.Version);
        Assert.Equal(ulong.MaxValue, decoded.Qid.Path);
        Assert.Equal(uint.MaxValue, decoded.Mode);
        Assert.Equal(uint.MaxValue, decoded.AccessTime);
        Assert.Equal(uint.MaxValue, decoded.ModificationTime);
        Assert.Equal(ulong.MaxValue, decoded.Length);
        Assert.Equal(string.Empty, decoded.Name);
        Assert.Equal(string.Empty, decoded.User);
        Assert.Equal(string.Empty, decoded.Group);
        Assert.Equal(string.Empty, decoded.LastModifier);
        Assert.Equal(49U, decoded.EncodedLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(48)]
    public void RecordsShorterThanStatFixLenAreRejected(int length)
        => Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(new byte[length]));

    [Fact]
    public void PrefixStringBoundsAndTrailingBytesAreCheckedExactly()
    {
        byte[] valid = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = "file" });
        byte[] wrongPrefix = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongPrefix, 1);
        byte[] oversizedName = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(oversizedName.AsSpan(41), ushort.MaxValue);
        byte[] trailing = new byte[valid.Length + 1];
        valid.CopyTo(trailing, 0);
        Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(wrongPrefix));
        Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(oversizedName));
        Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(trailing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("..")]
    public void KernelLimitedNameCheckPermitsNativeSpecialCases(string name)
        => Assert.True(FileStatRecords.ValidateAndCopyUpdate(
            FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = name })).Length >= 49);

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\u001fb")]
    [InlineData("a\u007fb")]
    public void KernelLimitedNameCheckRejectsShortForbiddenBytes(string name)
        => Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(
            FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = name })));

    [Fact]
    public void SixtyFourByteNameDefersValidationToProvider()
    {
        string name = new string('a', 62) + "/b";
        Assert.Equal(64, name.Length);
        Assert.True(FileStatRecords.ValidateAndCopyUpdate(
            FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = name })).Length >= 49);
    }

    [Theory]
    [InlineData("\0/x")]
    [InlineData("/x")]
    [InlineData("\u001fx")]
    [InlineData("\u007fx")]
    public void NameChecksPreserveFirstByteBoundaries(string name)
    {
        byte[] bytes = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = name });
        if (name[0] == '\0')
        {
            Assert.NotEmpty(FileStatRecords.ValidateAndCopyUpdate(bytes));
        }
        else
        {
            Assert.Throws<NamespaceFidException>(() => FileStatRecords.ValidateAndCopyUpdate(bytes));
        }
    }

    [Fact]
    public void TypedCodecRejectsInvalidUtf8AndNullRequests()
    {
        Assert.Throws<ArgumentNullException>(() => FileStatOperations.EncodeUpdate(null!));
        Assert.Throws<System.Text.EncoderFallbackException>(() => FileStatOperations.EncodeUpdate(
            ResourceWStat.Unchanged() with { Name = "\ud800" }));
        byte[] bytes = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Name = "x" });
        bytes[43] = 0xff;
        Assert.NotEmpty(FileStatRecords.ValidateAndCopyUpdate(bytes));
        Assert.Throws<NamespaceFidException>(() => FileStatRecords.DecodeUpdate(bytes));
    }

    [Fact]
    public void EncoderAcceptsTheFullUnsignedPayloadAndRejectsOneMoreByte()
    {
        string maximum = new('g', 65488);
        byte[] encoded = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with { Group = maximum });
        Assert.Equal(65537, encoded.Length);
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(encoded));
        Assert.Throws<IOException>(() => FileStatOperations.EncodeUpdate(
            ResourceWStat.Unchanged() with { Group = maximum + "g" }));
    }

    [Property(MaxTest = 100)]
    public bool NumericFieldsAndUtf8StringsRoundTrip(
        ushort type,
        uint device,
        byte qidType,
        uint version,
        ulong path,
        uint mode,
        uint atime,
        uint mtime,
        ulong length,
        NonNull<string> name,
        NonNull<string> group)
    {
        string safeName = Clean(name.Get, 30);
        string safeGroup = Clean(group.Get, 30);
        var value = new ResourceWStat(
            type,
            device,
            new Qid((QidType)qidType, version, path),
            mode,
            atime,
            mtime,
            length,
            safeName,
            string.Empty,
            safeGroup,
            string.Empty,
            0);
        byte[] bytes = FileStatOperations.EncodeUpdate(value);
        ResourceWStat decoded = FileStatRecords.DecodeUpdate(FileStatRecords.ValidateAndCopyUpdate(bytes));
        Assert.Equal(value with { EncodedLength = (uint)bytes.Length }, decoded);
        return true;
    }

    private static string Clean(string value, int limit)
        => new(value.Where(character => character >= ' ' && character != '/' && character != 127)
            .Take(limit).ToArray());
}

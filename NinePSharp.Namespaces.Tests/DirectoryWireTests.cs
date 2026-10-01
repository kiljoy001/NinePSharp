using System.Buffers.Binary;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class DirectoryWireTests
{
    [Fact]
    public async Task NativeWireSupportsFullUnsignedRecordPrefixWithoutTotalSizeWrapping()
    {
        await using var f = new StreamingDirectoryFixture();
        foreach (int size in new[] { 49, 65535, 65536, 65537 })
        {
            byte[] record = f.Record("", size);
            Assert.Equal(size - 2, BinaryPrimitives.ReadUInt16LittleEndian(record));
            DirectoryRecords.Validate(record, (uint)size);
        }
        Assert.Throws<IOException>(() => f.Record("", 65538));
    }

    [Fact]
    public void InvalidStringPrefixAndTrailingBytesAreRejected()
    {
        byte[] record = new byte[49];
        BinaryPrimitives.WriteUInt16LittleEndian(record, 47);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(41), 5);
        Assert.Throws<IOException>(() => DirectoryRecords.Validate(record, 49));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(41), 0);
        byte[] extra = new byte[50];
        record.CopyTo(extra, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(extra, 48);
        Assert.Throws<IOException>(() => DirectoryRecords.Validate(extra, 50));
        Assert.Throws<IOException>(() => DirectoryRecords.Validate(new byte[] { 1, 0 }, 2));
        DirectoryRecords.Validate(record, uint.MaxValue);
    }

    [Fact]
    public async Task StatAdapterUsesExplicitUniqueIdentityAndHonorsCount()
    {
        await using var f = new StreamingDirectoryFixture();
        ResourceStat original = await f.Files.Resources.StatAsync(f.Root, default);
        Assert.Equal(f.Root.Identity, f.Codec.ResolveIdentity(7, 0, f.Root.Identity.Path));
        Assert.Throws<IOException>(() => f.Codec.ResolveIdentity(8, 0, 1));
        Assert.Throws<IOException>(() => f.Codec.ResolveIdentity(7, 99, 1));
        Assert.Throws<IOException>(() => f.Codec.Encode(original with { Resource = new(new("unknown", "root", 1), 0) }));
        Assert.Throws<IOException>(() => f.Codec.Encode(original with { Resource = new(new("memory-data", "unknown", 1), 0) }));
        foreach (uint count in new uint[] { 0, 1, 2, 3 })
            Assert.Equal(Math.Min(2, (int)count), (await f.Codec.StatAsync(f.Root, count, default)).Length);
        byte[] bytes = f.Codec.Encode(original);
        Assert.Equal(bytes, (await f.Codec.StatAsync(f.Root, (uint)bytes.Length, default)).ToArray());
        Assert.Equal(bytes, (await f.Codec.StatAsync(f.Root, uint.MaxValue, default)).ToArray());
        var binding = new DirectoryDeviceBinding(7, 0, "memory-data", "root");
        Assert.Throws<ArgumentException>(() => new DirectoryStatOperations(f.Files.Resources,
            new[] { binding, binding with { Provider = "other" } }));
        Assert.Throws<ArgumentException>(() => new DirectoryStatOperations(f.Files.Resources,
            new[] { binding, binding with { Type = 8 } }));
        Assert.Throws<ArgumentNullException>(() => new DirectoryStatOperations(null!, new[] { binding }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Plan9FileSyscalls(f.Files.Process, f.Files.Plane, f.Files.Context, (DirectoryReadMode)17));
    }
}

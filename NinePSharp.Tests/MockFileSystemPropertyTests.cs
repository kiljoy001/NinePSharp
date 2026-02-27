using NinePSharp.Constants;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

public class MockFileSystemPropertyTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    [Property(MaxTest = 50)]
    public void MockFileSystem_FileCount_Matches_Statfs(PositiveInt fileCount)
    {
        var fs = new MockFileSystem(_vault);
        var actualCount = Math.Min(fileCount.Get, 100);

        // Create files
        for (int i = 0; i < actualCount; i++)
        {
            fs.LcreateAsync(new Tlcreate(100, (ushort)(i % 1000), 1, $"file{i}.txt", 0, 0644, 0)).Wait();
            fs.WalkAsync(new Twalk((ushort)(i % 1000), 1, 1, new[] { ".." })).Wait();
        }

        // Get statfs
        var statfs = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Verify: files count should be actualCount + 1 (root directory)
        statfs.Files.Should().Be((ulong)(actualCount + 1));
    }

    [Property(MaxTest = 30)]
    public void MockFileSystem_Rename_Preserves_FileCount(NonEmptyString oldName, NonEmptyString newName)
    {
        if (oldName.Get == newName.Get || string.IsNullOrWhiteSpace(oldName.Get) || string.IsNullOrWhiteSpace(newName.Get))
            return; // Skip if same name or invalid

        var fs = new MockFileSystem(_vault);

        // Create a file
        fs.LcreateAsync(new Tlcreate(100, 1, 1, oldName.Get, 0, 0644, 0)).Wait();
        var statfsBefore = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Rename it
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { oldName.Get })).Wait();
        fs.RenameAsync(new Trename(100, 1, 2, 1, newName.Get)).Wait();

        // Check file count is preserved
        fs.WalkAsync(new Twalk(2, 2, 3, new[] { ".." })).Wait();
        var statfsAfter = fs.StatfsAsync(new Tstatfs(100, 3, 3)).Result;

        statfsBefore.Files.Should().Be(statfsAfter.Files);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_Clone_Is_Independent(PositiveInt fileCount)
    {
        var fs1 = new MockFileSystem(_vault);
        var actualCount = Math.Min(fileCount.Get, 20);

        // Create files in fs1
        for (int i = 0; i < actualCount; i++)
        {
            fs1.LcreateAsync(new Tlcreate(100, (ushort)(i % 1000), 1, $"file{i}.txt", 0, 0644, 0)).Wait();
            fs1.WalkAsync(new Twalk((ushort)(i % 1000), 1, 1, new[] { ".." })).Wait();
        }

        var statfs1Before = fs1.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Clone
        var fs2 = (MockFileSystem)fs1.Clone();

        // Modify fs1 by adding more files
        fs1.LcreateAsync(new Tlcreate(100, 999, 1, "newfile.txt", 0, 0644, 0)).Wait();

        // Check fs2 wasn't affected
        var statfs2 = fs2.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        statfs1Before.Files.Should().Be(statfs2.Files);
    }

    [Property(MaxTest = 30)]
    public void MockFileSystem_Mkdir_Increases_FileCount(NonEmptyString dirName)
    {
        if (string.IsNullOrWhiteSpace(dirName.Get)) return;

        var fs = new MockFileSystem(_vault);

        var statfsBefore = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Create directory
        fs.MkdirAsync(new Tmkdir(100, 1, 1, dirName.Get, 0755, 0)).Wait();

        var statfsAfter = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // File count should increase by 1
        statfsAfter.Files.Should().Be(statfsBefore.Files + 1);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_Readdir_Offset_Pagination_Is_Consistent(PositiveInt fileCount)
    {
        var fs = new MockFileSystem(_vault);
        var count = Math.Min(fileCount.Get, 10);

        // Create multiple files
        for (int i = 0; i < count; i++)
        {
            fs.LcreateAsync(new Tlcreate(100, (ushort)(i % 1000), 1, $"file{i}.txt", 0, 0644, 0)).Wait();
            fs.WalkAsync(new Twalk((ushort)(i % 1000), 1, 1, new[] { ".." })).Wait();
        }

        // Read all at once
        var resultAll = fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 65536)).Result;

        // Read with offset should return less or equal data
        var resultWithOffset = fs.ReaddirAsync(new Treaddir(100, 1, 1, 10, 65536)).Result;

        resultWithOffset.Count.Should().BeLessThanOrEqualTo(resultAll.Count);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_Write_Then_Read_Returns_Same_Data(NonEmptyArray<byte> data)
    {
        if (data.Get.Length == 0 || data.Get.Length > 1024) return;

        var fs = new MockFileSystem(_vault);
        var testData = data.Get;

        // Create file
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "testfile.txt", 0, 0644, 0)).Wait();
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "testfile.txt" })).Wait();

        // Write data
        fs.WriteAsync(new Twrite(1, 2, 0, testData)).Wait();

        // Read data back
        var readResult = fs.ReadAsync(new Tread(1, 2, 0, (uint)testData.Length)).Result;

        // Data should match
        readResult.Data.ToArray().Should().Equal(testData);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_StatfsAsync_BSize_Is_Constant(PositiveInt fileCount)
    {
        var fs = new MockFileSystem(_vault);
        var count = Math.Min(fileCount.Get, 20);

        // Create varying number of files
        for (int i = 0; i < count; i++)
        {
            fs.LcreateAsync(new Tlcreate(100, (ushort)(i % 1000), 1, $"file{i}.txt", 0, 0644, 0)).Wait();
            fs.WalkAsync(new Twalk((ushort)(i % 1000), 1, 1, new[] { ".." })).Wait();
        }

        var statfs = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // BSize should always be 4096
        statfs.BSize.Should().Be(4096);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_Multiple_Writes_Append_Correctly(PositiveInt writeCount)
    {
        var fs = new MockFileSystem(_vault);
        var numWrites = Math.Min(writeCount.Get, 10);

        // Create file
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "testfile.txt", 0, 0644, 0)).Wait();
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "testfile.txt" })).Wait();

        ulong totalBytesWritten = 0;

        // Write multiple times at different offsets
        for (int i = 0; i < numWrites; i++)
        {
            var writeData = new byte[] { (byte)i };
            fs.WriteAsync(new Twrite(1, 2, totalBytesWritten, writeData)).Wait();
            totalBytesWritten += (ulong)writeData.Length;
        }

        // Read back and verify size
        var readResult = fs.ReadAsync(new Tread(1, 2, 0, (uint)totalBytesWritten)).Result;

        readResult.Data.Length.Should().Be((int)totalBytesWritten);
    }

    [Property(MaxTest = 20)]
    public void MockFileSystem_Renameat_Preserves_FileCount(NonEmptyString oldName, NonEmptyString newName)
    {
        if (oldName.Get == newName.Get || string.IsNullOrWhiteSpace(oldName.Get) || string.IsNullOrWhiteSpace(newName.Get))
            return;

        var fs = new MockFileSystem(_vault);

        // Create file
        fs.LcreateAsync(new Tlcreate(100, 1, 1, oldName.Get, 0, 0644, 0)).Wait();
        var statfsBefore = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Renameat
        fs.RenameatAsync(new Trenameat(100, 1, 1, oldName.Get, 1, newName.Get)).Wait();

        var statfsAfter = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // File count should be unchanged
        statfsBefore.Files.Should().Be(statfsAfter.Files);
    }

    [Fact]
    public async Task MockFileSystem_Property_Tests_Can_Run_Synchronously()
    {
        // This is a smoke test to ensure property tests infrastructure works
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "test.txt", 0, 0644, 0));
        var statfs = await fs.StatfsAsync(new Tstatfs(100, 1, 1));
        statfs.Files.Should().Be(2); // root + 1 file
    }
}

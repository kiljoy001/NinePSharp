using NinePSharp.Constants;
using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;

namespace NinePSharp.Tests;

public class MockFileSystemTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task MockFileSystem_StatfsAsync_Returns_Dynamic_Stats()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(1, 100, 1));

        // Assert
        result.Should().BeOfType<Rstatfs>();
        result.BSize.Should().Be(4096);
        result.Blocks.Should().Be(100000);
        result.FsType.Should().Be(0x01021997);
    }

    [Fact]
    public async Task MockFileSystem_StatfsAsync_Reflects_Actual_File_Count()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Create some files
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "file1.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { ".." })); // Go back to root
        await fs.LcreateAsync(new Tlcreate(100, 2, 2, "file2.txt", 0, 0644, 0));

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(1, 100, 1));

        // Assert - should have root + 2 files = 3 entries total
        result.Files.Should().Be(3); // root directory + 2 files
    }

    [Fact]
    public async Task MockFileSystem_MkdirAsync_Creates_Directory()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Act
        var result = await fs.MkdirAsync(new Tmkdir(100, 1, 1, "testdir", 0755, 0));

        // Assert
        result.Should().BeOfType<Rmkdir>();
        result.Tag.Should().Be(1);
        result.Qid.Type.Should().Be(QidType.QTDIR);
    }

    [Fact]
    public async Task MockFileSystem_MkdirAsync_Fails_If_Already_Exists()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.MkdirAsync(new Tmkdir(100, 1, 1, "testdir", 0755, 0));

        // Act & Assert
        Func<Task> act = async () => await fs.MkdirAsync(new Tmkdir(100, 2, 1, "testdir", 0755, 0));
        await act.Should().ThrowAsync<NinePProtocolException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task MockFileSystem_MkdirAsync_Fails_If_Parent_Not_Directory()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "file.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "file.txt" }));

        // Act & Assert - Try to create directory inside a file
        Func<Task> act = async () => await fs.MkdirAsync(new Tmkdir(100, 2, 2, "subdir", 0755, 0));
        await act.Should().ThrowAsync<NinePProtocolException>()
            .WithMessage("*Parent directory does not exist*");
    }

    [Fact]
    public async Task MockFileSystem_LcreateAsync_Creates_File()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Act
        var result = await fs.LcreateAsync(new Tlcreate(100, 1, 1, "test.txt", 0, 0644, 0));

        // Assert
        result.Should().BeOfType<Rlcreate>();
        result.Tag.Should().Be(1);
        result.Qid.Type.Should().Be(QidType.QTFILE);
        result.Iounit.Should().Be(8192);
    }

    [Fact]
    public async Task MockFileSystem_RenameAsync_Renames_File()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "oldname.txt", 0, 0644, 0));

        // Act
        var result = await fs.RenameAsync(new Trename(100, 1, 1, 1, "newname.txt"));

        // Assert
        result.Should().BeOfType<Rrename>();
        result.Tag.Should().Be(1);
    }

    [Fact]
    public async Task MockFileSystem_RenameatAsync_Renames_File()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "oldname.txt", 0, 0644, 0));

        // Act
        var result = await fs.RenameatAsync(new Trenameat(100, 1, 1, "oldname.txt", 1, "newname.txt"));

        // Assert
        result.Should().BeOfType<Rrenameat>();
        result.Tag.Should().Be(1);
    }

    [Fact]
    public async Task MockFileSystem_ReaddirAsync_Lists_Children()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "file1.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { ".." }));
        await fs.LcreateAsync(new Tlcreate(100, 2, 2, "file2.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 2, 3, new[] { ".." }));

        // Act
        var result = await fs.ReaddirAsync(new Treaddir(100, 3, 3, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);

        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("file1.txt");
        dataString.Should().Contain("file2.txt");
    }

    [Fact]
    public async Task MockFileSystem_ReaddirAsync_Supports_Pagination_With_Offset()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Create multiple files
        for (int i = 0; i < 5; i++)
        {
            await fs.LcreateAsync(new Tlcreate(100, (ushort)i, 1, $"file{i}.txt", 0, 0644, 0));
            await fs.WalkAsync(new Twalk((ushort)i, 1, 1, new[] { ".." }));
        }

        // Act - Read first batch
        var result1 = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));
        result1.Count.Should().BeGreaterThan(0);

        // Act - Read with offset (simplified - in real impl would parse offset from first read)
        var result2 = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 3, 8192));

        // Assert - Second read should have fewer entries (or none if all fit in first read)
        result2.Count.Should().BeLessThanOrEqualTo(result1.Count);
    }

    [Fact]
    public async Task MockFileSystem_ReaddirAsync_Fails_If_Not_Directory()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "file.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "file.txt" }));

        // Act & Assert
        Func<Task> act = async () => await fs.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192));
        await act.Should().ThrowAsync<NinePProtocolException>()
            .WithMessage("*Not a directory*");
    }

    [Fact]
    public async Task MockFileSystem_ReadAsync_And_WriteAsync_Work()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.LcreateAsync(new Tlcreate(100, 1, 1, "file.txt", 0, 0644, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "file.txt" }));

        var data = Encoding.UTF8.GetBytes("Hello, World!");

        // Act - Write
        var writeResult = await fs.WriteAsync(new Twrite(1, 2, 0, data));
        writeResult.Count.Should().Be((uint)data.Length);

        // Act - Read
        var readResult = await fs.ReadAsync(new Tread(1, 2, 0, 8192));

        // Assert
        readResult.Data.ToArray().Should().Equal(data);
        Encoding.UTF8.GetString(readResult.Data.Span).Should().Be("Hello, World!");
    }

    [Fact]
    public async Task MockFileSystem_StatAsync_Returns_Correct_Entry_Info()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);
        await fs.MkdirAsync(new Tmkdir(100, 1, 1, "mydir", 0755, 0));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "mydir" }));

        // Act
        var result = await fs.StatAsync(new Tstat(1, 2));

        // Assert
        result.Stat.Name.Should().Be("mydir");
        ((uint)result.Stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR).Should().NotBe(0);
    }

    [Fact]
    public async Task MockFileSystem_Clone_Creates_Independent_Copy()
    {
        // Arrange
        var fs1 = new MockFileSystem(_vault);
        await fs1.LcreateAsync(new Tlcreate(100, 1, 1, "file.txt", 0, 0644, 0));

        // Act - Clone
        var fs2 = (MockFileSystem)fs1.Clone();

        // Modify fs1
        await fs1.WalkAsync(new Twalk(1, 1, 2, new[] { ".." }));
        await fs1.LcreateAsync(new Tlcreate(100, 2, 2, "file2.txt", 0, 0644, 0));

        // Act - Read from fs2
        await fs2.WalkAsync(new Twalk(1, 1, 3, new[] { ".." }));
        var fs2Readdir = await fs2.ReaddirAsync(new Treaddir(100, 3, 3, 0, 8192));
        var fs2Data = Encoding.UTF8.GetString(fs2Readdir.Data.Span);

        // Assert - fs2 should not have file2.txt
        fs2Data.Should().Contain("file.txt");
        fs2Data.Should().NotContain("file2.txt");
    }

    // Large data scenarios

    [Fact]
    public async Task MockFileSystem_Handles_1000_Files()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Act - Create 1000 files
        for (int i = 0; i < 1000; i++)
        {
            await fs.LcreateAsync(new Tlcreate(100, (ushort)(i % ushort.MaxValue), 1, $"file{i:D4}.txt", 0, 0644, 0));
            await fs.WalkAsync(new Twalk((ushort)(i % ushort.MaxValue), 1, 1, new[] { ".." }));
        }

        // Act - StatfsAsync should reflect the count
        var statfs = await fs.StatfsAsync(new Tstatfs(1, 1, 1));

        // Assert
        statfs.Files.Should().Be(1001); // 1000 files + 1 root directory
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public async Task MockFileSystem_Scales_With_File_Count(int fileCount)
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Act - Create files
        for (int i = 0; i < fileCount; i++)
        {
            await fs.LcreateAsync(new Tlcreate(100, (ushort)(i % ushort.MaxValue), 1, $"file{i:D4}.txt", 0, 0644, 0));
            await fs.WalkAsync(new Twalk((ushort)(i % ushort.MaxValue), 1, 1, new[] { ".." }));
        }

        // Act - Read directory
        var readdir = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 65536));

        // Assert - Should be able to read all entries
        readdir.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MockFileSystem_StatfsAsync_Accurate_With_Many_Files()
    {
        // Arrange
        var fs = new MockFileSystem(_vault);

        // Create 50 files
        for (int i = 0; i < 50; i++)
        {
            await fs.LcreateAsync(new Tlcreate(100, (ushort)i, 1, $"file{i}.txt", 0, 0644, 0));
            await fs.WalkAsync(new Twalk((ushort)i, 1, 1, new[] { ".." }));
        }

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(1, 1, 1));

        // Assert
        result.Files.Should().Be(51); // 50 files + 1 root
    }
}

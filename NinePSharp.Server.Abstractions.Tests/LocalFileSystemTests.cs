using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.FileSystem;
using Xunit;

namespace NinePSharp.Server.Abstractions.Tests;

public class LocalFileSystemTests : IDisposable
{
    private readonly string _testRoot;

    public LocalFileSystemTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "NinePSharp_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Fact]
    public async Task LocalDir_Readdir_ReturnsDiskEntries()
    {
        File.WriteAllText(Path.Combine(_testRoot, "file1.txt"), "content1");
        Directory.CreateDirectory(Path.Combine(_testRoot, "subdir"));

        var localDir = new LocalDir(new DirectoryInfo(_testRoot));
        var entries = (await localDir.ReaddirAsync(default)).ToList();

        Assert.Contains(entries, e => e.Name == "file1.txt");
        Assert.Contains(entries, e => e.Name == "subdir");
    }

    [Fact]
    public async Task LocalFile_ReadWrite_Works()
    {
        string filePath = Path.Combine(_testRoot, "rw.txt");
        var fi = new FileInfo(filePath);
        var localFile = new LocalFile(fi);

        byte[] content = Encoding.UTF8.GetBytes("9P on Disk");
        await localFile.WriteAsync(0, content, default);

        var read = await localFile.ReadAsync(0, (uint)content.Length, default);
        Assert.Equal("9P on Disk", Encoding.UTF8.GetString(read));
        Assert.Equal("9P on Disk", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task LocalDir_CreateAndRemove_Works()
    {
        var localDir = new LocalDir(new DirectoryInfo(_testRoot));
        
        var newNode = await localDir.CreateAsync("newfile.txt", 0644, 0, default);
        Assert.True(File.Exists(Path.Combine(_testRoot, "newfile.txt")));

        await localDir.RemoveAsync("newfile.txt", default);
        Assert.False(File.Exists(Path.Combine(_testRoot, "newfile.txt")));
    }

    [Fact]
    public async Task LocalDir_Walk_ReturnsParentAndNullForMissingEntries()
    {
        var childPath = Path.Combine(_testRoot, "subdir");
        Directory.CreateDirectory(childPath);

        var childDir = new LocalDir(new DirectoryInfo(childPath));

        var parent = await childDir.WalkAsync("..", default);
        var missing = await childDir.WalkAsync("missing.txt", default);

        Assert.NotNull(parent);
        Assert.Equal(new DirectoryInfo(_testRoot).Name, parent!.Name);
        Assert.Null(missing);
    }

    [Fact]
    public async Task LocalNode_Wstat_RenamesUnderlyingFile()
    {
        var oldPath = Path.Combine(_testRoot, "before.txt");
        File.WriteAllText(oldPath, "content");

        var localFile = new LocalFile(new FileInfo(oldPath));
        var qid = localFile.GetStat(NinePDialect.NineP2000).Qid;
        var stat = new Stat(
            0,
            0,
            0,
            qid,
            uint.MaxValue,
            uint.MaxValue,
            uint.MaxValue,
            ulong.MaxValue,
            "after.txt",
            "root",
            "root",
            "root",
            NinePDialect.NineP2000);

        await localFile.WstatAsync(stat, default);

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(Path.Combine(_testRoot, "after.txt")));
    }
}

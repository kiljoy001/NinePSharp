using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.FileSystem;
using Xunit;

namespace NinePSharp.Server.Abstractions.Tests;

public class FileSystemTests
{
    [Fact]
    public async Task NinePFile_ReadWrite_Works()
    {
        var file = new NinePFile("test.txt");
        var content = Encoding.UTF8.GetBytes("Hello World");
        
        await file.WriteAsync(0, content, default);
        var read = await file.ReadAsync(0, 5, default);
        
        Assert.Equal("Hello", Encoding.UTF8.GetString(read));
    }

    [Fact]
    public async Task NinePDir_Walk_Works()
    {
        var root = new NinePDir("/");
        var file = new NinePFile("hello.txt");
        root.AddChild(file);
        
        var found = await root.WalkAsync("hello.txt", default);
        Assert.Same(file, found);
    }

    [Fact]
    public async Task FileSystemBackend_ResolvePath_Works()
    {
        var root = new NinePDir("/");
        var sub = new NinePDir("sub");
        root.AddChild(sub);
        var file = new NinePFile("file.txt");
        sub.AddChild(file);
        
        var backend = new FileSystemBackend(root);
        backend.Dialect = NinePDialect.NineP2000L;

        var rstat = await backend.StatAsync(new[] { "sub", "file.txt" }, new Tstat(1, 1), NinePDialect.NineP2000L);
        
        Assert.Equal("file.txt", rstat.Stat.Name);
    }

    [Fact]
    public async Task FileSystemBackend_Readdir_ReturnsEntries()
    {
        var root = new NinePDir("/");
        root.AddChild(new NinePFile("f1"));
        root.AddChild(new NinePFile("f2"));
        
        var backend = new FileSystemBackend(root);
        backend.Dialect = NinePDialect.NineP2000L;

        var rreaddir = await backend.ReaddirAsync(new string[0], new Treaddir(24, 1, 1, 0, 1000), NinePDialect.NineP2000L);
        
        Assert.True(rreaddir.Count > 0);
    }

    [Fact]
    public async Task NinePNode_Symlink_Works()
    {
        var root = new NinePDir("/");
        await root.SymlinkAsync("link", "target", default);
        var linkNode = await root.WalkAsync("link", default);
        Assert.NotNull(linkNode);
        Assert.Equal("target", await linkNode.ReadlinkAsync(default));
    }

    [Fact]
    public async Task NinePNode_Hardlink_Works()
    {
        var root = new NinePDir("/");
        var file = new NinePFile("original");
        root.AddChild(file);
        await root.LinkAsync("hardlink", file, default);
        var linkNode = await root.WalkAsync("hardlink", default);
        Assert.NotNull(linkNode);
        Assert.Equal(file.GetStat(NinePDialect.NineP2000L).Qid.Path, linkNode.GetStat(NinePDialect.NineP2000L).Qid.Path);
    }
}

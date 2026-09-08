using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Constants;
using NinePSharp.Examples;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Tests;

public class InMemoryHandlerTests
{
    [Fact]
    public async Task WalkAsync_ShouldReturnQidsForValidPath()
    {
        var handler = new InMemoryHandler();
        handler.AddFile("/a/b/c.txt", "content");

        var walkMsg = new Twalk(1, 1, 2, new[] { "a", "b", "c.txt" });
        var ct = CancellationToken.None;

        var result = await handler.WalkAsync(System.Array.Empty<string>(), walkMsg, ct);

        result.Wqid.Should().HaveCount(3);
        result.Wqid[2].Type.Should().Be(QidType.QTFILE);
    }

    [Fact]
    public async Task WalkAsync_ShouldReturnFewerQidsForPartialPath()
    {
        var handler = new InMemoryHandler();
        handler.AddDirectory("/a/b");

        var walkMsg = new Twalk(1, 1, 2, new[] { "a", "b", "missing" });
        var ct = CancellationToken.None;

        var result = await handler.WalkAsync(System.Array.Empty<string>(), walkMsg, ct);

        result.Wqid.Should().HaveCount(2);
        result.Wqid[1].Type.Should().Be(QidType.QTDIR);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnFileContent()
    {
        var handler = new InMemoryHandler();
        var content = "hello world";
        handler.AddFile("/test.txt", content);

        var readMsg = new Tread(1, 1, 0, 100);
        var ct = CancellationToken.None;

        var result = await handler.ReadAsync(new[] { "test.txt" }, readMsg, ct);

        var text = Encoding.UTF8.GetString(result.Data.ToArray());
        text.Should().Be(content);
    }

    [Fact]
    public async Task WriteAsync_ShouldUpdateFileContent()
    {
        var handler = new InMemoryHandler();
        handler.AddFile("/target.txt", "initial");

        var newContent = Encoding.UTF8.GetBytes("updated");
        var writeMsg = new Twrite(1, 1, 0, newContent);
        var ct = CancellationToken.None;

        await handler.WriteAsync(new[] { "target.txt" }, writeMsg, ct);

        var readMsg = new Tread(2, 1, 0, 100);
        var readResult = await handler.ReadAsync(new[] { "target.txt" }, readMsg, ct);

        var text = Encoding.UTF8.GetString(readResult.Data.ToArray());
        text.Should().Be("updated");
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateNewFile()
    {
        var handler = new InMemoryHandler();
        handler.AddDirectory("/parent");

        var createMsg = new Tcreate(1, 1, "new.txt", NinePConstants.Mode0644, 2);
        var ct = CancellationToken.None;

        var result = await handler.CreateAsync(new[] { "parent" }, createMsg, ct);
        result.Qid.Type.Should().Be(QidType.QTFILE);

        var walkMsg = new Twalk(2, 1, 2, new[] { "new.txt" });
        var walkResult = await handler.WalkAsync(new[] { "parent" }, walkMsg, ct);

        walkResult.Wqid.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteFile()
    {
        var handler = new InMemoryHandler();
        handler.AddFile("/todelete.txt", "content");

        var removeMsg = new Tremove(1, 1);
        var ct = CancellationToken.None;

        await handler.RemoveAsync(new[] { "todelete.txt" }, removeMsg, ct);

        var walkMsg = new Twalk(2, 1, 2, new[] { "todelete.txt" });
        var act = async () => await handler.WalkAsync(System.Array.Empty<string>(), walkMsg, ct);

        // First segment will be "todelete.txt", which is missing. Since it's the very first
        // segment of the root, Walk returns fewer items normally, but wait, if root exists,
        // asking for missing children just stops and returns empty Qids.
        var walkResult = await act();
        walkResult.Wqid.Should().BeEmpty();
    }

    [Fact]
    public async Task StatAsync_ShouldUsePlan9PermissionBits()
    {
        var handler = new InMemoryHandler();
        handler.AddFile("/file.txt", "content");

        var root = await handler.StatAsync(System.Array.Empty<string>(), new Tstat(1, 1), default);
        var file = await handler.StatAsync(new[] { "file.txt" }, new Tstat(2, 2), default);

        root.Stat.Mode.Should().Be((uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0777);
        file.Stat.Mode.Should().Be(NinePConstants.Mode0644);
    }
}

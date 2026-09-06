using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Tests.Helpers;

namespace NinePSharp.Tests;

public class NinePFSDispatcherTests
{
    [Fact]
    public async Task Constructor_WithDirectHandler_DispatchesMessages()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new MarkerFileSystem("alpha"));

        var response = await dispatcher.DispatchAsync(
            "session-1",
            NinePMessage.NewMsgTversion(new Tversion(1, 8192, "9P2000")),
            NinePDialect.NineP2000);

        var version = Assert.IsType<Rversion>(response);
        Assert.Equal((ushort)1, version.Tag);
        Assert.Equal("9P2000", version.Version);
    }

    [Fact]
    public async Task Dispatcher_Delegates_Extended_File_Operations()
    {
        var handler = new ExtendedOperationHandler();
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            handler);

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 1);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 2, ["target"]);

        await DispatchAsync<Rwstat>(dispatcher, NinePMessage.NewMsgTwstat(new Twstat(3, 2, CreateStat("target"))));
        Assert.Equal("wstat:target", handler.LastCall);

        await DispatchAsync<Rsymlink>(dispatcher, NinePMessage.NewMsgTsymlink(new Tsymlink(SizeWithStrings(8, "link", "target"), 4, 1, "link", "target", 0)));
        Assert.Equal("symlink:", handler.LastCall);

        await DispatchAsync<Rreadlink>(dispatcher, NinePMessage.NewMsgTreadlink(new Treadlink((uint)(NinePConstants.HeaderSize + 4), 5, 2)));
        Assert.Equal("readlink:target", handler.LastCall);

        await DispatchAsync<Rlink>(dispatcher, NinePMessage.NewMsgTlink(new Tlink(SizeWithStrings(8, "hard"), 6, 1, 2, "hard")));
        Assert.Equal("link:", handler.LastCall);

        await DispatchAsync<Rlerror>(dispatcher, NinePMessage.NewMsgTlock(new Tlock(SizeWithStrings(29, "client"), 7, 2, 0, 0, 0, 0, 0, "client")));
        Assert.Equal("lock:target", handler.LastCall);

        await DispatchAsync<Rgetlock>(dispatcher, NinePMessage.NewMsgTgetlock(new Tgetlock(SizeWithStrings(25, "client"), 8, 2, 0, 0, 0, 0, "client")));
        Assert.Equal("getlock:target", handler.LastCall);

        await DispatchAsync<Rxattrwalk>(dispatcher, NinePMessage.NewMsgTxattrwalk(new Txattrwalk(SizeWithStrings(8, "user.note"), 9, 2, 3, "user.note")));
        Assert.Equal("xattrwalk:target", handler.LastCall);

        await DispatchAsync<Rstat>(dispatcher, NinePMessage.NewMsgTstat(new Tstat(10, 3)));
        Assert.Equal("stat:target", handler.LastCall);

        await DispatchAsync<Rxattrcreate>(dispatcher, NinePMessage.NewMsgTxattrcreate(new Txattrcreate(SizeWithStrings(16, "user.note"), 11, 2, "user.note", 12, 0)));
        Assert.Equal("xattrcreate:target", handler.LastCall);
    }

    private static async Task<T> DispatchAsync<T>(NinePFSDispatcher dispatcher, NinePMessage message)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            message,
            NinePDialect.NineP2000L);

        return Assert.IsType<T>(response);
    }

    private static uint SizeWithStrings(int fixedBytes, params string[] values)
    {
        var stringsSize = values.Sum(value => 2 + Encoding.UTF8.GetByteCount(value));
        return (uint)(NinePConstants.HeaderSize + fixedBytes + stringsSize);
    }

    private static Stat CreateStat(string name)
        => new(0, 0, 0, new Qid(QidType.QTFILE, 0, 42), 0644, 0, 0, 0, name, "user", "group", "user", NinePDialect.NineP2000);

    private sealed class ExtendedOperationHandler : TestHandlerBase
    {
        public string LastCall { get; private set; } = string.Empty;

        public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
        {
            LastCall = FormatCall("walk", relativePath);
            var qids = msg.Wname.Select((_, index) => new Qid(QidType.QTFILE, 0, (ulong)(index + 1))).ToArray();
            return Task.FromResult(new Rwalk(msg.Tag, qids));
        }

        public override Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
        {
            LastCall = FormatCall("open", relativePath);
            return Task.FromResult(new Ropen(msg.Tag, new Qid(QidType.QTFILE, 0, 42), 0));
        }

        public override Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
        {
            LastCall = FormatCall("read", relativePath);
            return Task.FromResult(new Rread(msg.Tag, Encoding.UTF8.GetBytes(JoinPath(relativePath))));
        }

        public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct)
        {
            LastCall = FormatCall("write", relativePath);
            return Task.FromResult(new Rwrite(msg.Tag, (uint)msg.Data.Length));
        }

        public override Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct)
        {
            LastCall = FormatCall("stat", relativePath);
            return Task.FromResult(new Rstat(msg.Tag, CreateStat(relativePath.LastOrDefault() ?? string.Empty)));
        }

        public override Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct)
        {
            LastCall = FormatCall("wstat", relativePath);
            return Task.FromResult(new Rwstat(msg.Tag));
        }

        public override Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct)
        {
            LastCall = FormatCall("symlink", relativePath);
            return Task.FromResult(new Rsymlink((uint)(NinePConstants.HeaderSize + 13), msg.Tag, new Qid(QidType.QTFILE, 0, 43)));
        }

        public override Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct)
        {
            LastCall = FormatCall("readlink", relativePath);
            return Task.FromResult(new Rreadlink(SizeWithStrings(0, "target"), msg.Tag, "target"));
        }

        public override Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct)
        {
            LastCall = FormatCall("link", relativePath);
            return Task.FromResult(new Rlink((uint)NinePConstants.HeaderSize, msg.Tag));
        }

        public override Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct)
        {
            LastCall = FormatCall("lock", relativePath);
            return Task.FromResult(new Rlerror(msg.Tag, 0));
        }

        public override Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct)
        {
            LastCall = FormatCall("getlock", relativePath);
            return Task.FromResult(new Rgetlock(SizeWithStrings(21, "client"), msg.Tag, 0, 0, 0, 0, "client"));
        }

        public override Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct)
        {
            LastCall = FormatCall("xattrwalk", relativePath);
            return Task.FromResult(new Rxattrwalk((uint)(NinePConstants.HeaderSize + 8), msg.Tag, 12));
        }

        public override Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct)
        {
            LastCall = FormatCall("xattrcreate", relativePath);
            return Task.FromResult(new Rxattrcreate((uint)NinePConstants.HeaderSize, msg.Tag));
        }

        private static string FormatCall(string operation, string[] relativePath)
            => $"{operation}:{JoinPath(relativePath)}";

        private static string JoinPath(string[] relativePath)
            => string.Join("/", relativePath);
    }
}

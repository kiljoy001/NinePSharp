using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Examples;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Tests.Helpers;
using Xunit;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests;

public class AuthFileIoTests
{
    private class AuthTestHandler : TestHandlerBase, IAuthHandler
    {
        public byte[] LastWritten { get; private set; } = System.Array.Empty<byte>();

        public override Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct)
            => Task.FromResult<IAuthHandler?>(this);

        public Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
        {
            return Task.FromResult(Encoding.UTF8.GetBytes("challenge"));
        }

        public Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
        {
            LastWritten = data;
            return Task.FromResult((uint)data.Length);
        }

        public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => throw new System.NotImplementedException();

        public override Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => throw new System.NotImplementedException();
        public override Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => Task.FromResult(new Rflush(msg.Tag));
    }

    [Fact]
    public async Task Auth_Read_And_Write_Should_Be_Dispatched_To_Handler()
    {
        var handler = new AuthTestHandler();
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, handler);

        var authResponse = await dispatcher.DispatchAsync(
            "auth-session",
            NinePMessage.NewMsgTauth(new Tauth(1, 42, "scott", string.Empty, null)),
            NinePDialect.NineP2000);

        var auth = Assert.IsType<Rauth>(authResponse);
        auth.Aqid.Type.Should().Be(QidType.QTAUTH);
        auth.Aqid.Path.Should().Be(42);

        var readResponse = await dispatcher.DispatchAsync(
            "auth-session",
            NinePMessage.NewMsgTread(new Tread(2, 42, 0, 128)),
            NinePDialect.NineP2000);

        var read = Assert.IsType<Rread>(readResponse);
        Encoding.UTF8.GetString(read.Data.Span).Should().Be("challenge");

        var payload = Encoding.UTF8.GetBytes("response");
        var writeResponse = await dispatcher.DispatchAsync(
            "auth-session",
            NinePMessage.NewMsgTwrite(new Twrite(3, 42, 0, payload)),
            NinePDialect.NineP2000);

        Assert.IsType<Rwrite>(writeResponse).Count.Should().Be((uint)payload.Length);
        handler.LastWritten.Should().Equal(payload);

        var clunkResponse = await dispatcher.DispatchAsync(
            "auth-session",
            NinePMessage.NewMsgTclunk(new Tclunk(4, 42)),
            NinePDialect.NineP2000);

        Assert.IsType<Rclunk>(clunkResponse);
    }

    [Fact]
    public async Task Auth_Should_Return_Error_When_Handler_Does_Not_Require_Authentication()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new InMemoryHandler());

        var response = await dispatcher.DispatchAsync(
            "no-auth-session",
            NinePMessage.NewMsgTauth(new Tauth(7, 42, "scott", string.Empty, null)),
            NinePDialect.NineP2000);

        var error = Assert.IsType<Rerror>(response);
        error.Tag.Should().Be(7);
        error.Ename.Should().Be("Authentication is not required.");
    }
}

using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Tests.Helpers;
using Xunit;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests;

public class AuthFlushAdversarialTests
{
    private class SlowAuthHandler : TestHandlerBase, IAuthHandler
    {
        private readonly int _delayMs;

        public SlowAuthHandler(int delayMs)
        {
            _delayMs = delayMs;
        }

        public override Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) 
            => Task.FromResult<IAuthHandler?>(this);

        public async Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return System.Array.Empty<byte>();
        }

        public async Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return (uint)data.Length;
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
    public void Test_Stub()
    {
        // Compilation fix
    }
}

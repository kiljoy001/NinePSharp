using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class NinePFSDispatcherPropertyTests
{
    [Property(MaxTest = 100)]
    public bool Dispatcher_Fuzz_Sequence_Returns_Valid_Responses(int[] seeds)
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        foreach (var msg in (seeds ?? Array.Empty<int>()).Take(32).Select(CreateMessage))
        {
            try {
                dispatcher.DispatchAsync("s1", msg, NinePDialect.NineP2000).Wait();
            } catch {
                return false;
            }
        }
        return true;
    }

    private static NinePMessage CreateMessage(int seed)
    {
        unchecked
        {
            ushort tag = (ushort)(Math.Abs(seed) % ushort.MaxValue);
            uint fid = (uint)(Math.Abs(seed) % 16);
            ulong offset = (ulong)(Math.Abs(seed) % 128);
            uint count = (uint)(Math.Abs(seed) % 256);
            string name = "n" + Math.Abs(seed % 100).ToString();

            return Math.Abs(seed % 10) switch
            {
                0 => NinePMessage.NewMsgTversion(new Tversion(tag, 8192, "9P2000")),
                1 => NinePMessage.NewMsgTauth(new Tauth(tag, fid, "user", "/mock")),
                2 => NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", "/")),
                3 => NinePMessage.NewMsgTwalk(new Twalk(tag, fid, (uint)((fid + 1) % 16), new[] { name })),
                4 => NinePMessage.NewMsgTopen(new Topen(tag, fid, NinePConstants.OREAD)),
                5 => NinePMessage.NewMsgTread(new Tread(tag, fid, offset, count)),
                6 => NinePMessage.NewMsgTwrite(new Twrite(tag, fid, offset, new byte[] { 1, 2, 3 })),
                7 => NinePMessage.NewMsgTreaddir(new Treaddir(24, tag, fid, offset, Math.Max(24u, count))),
                8 => NinePMessage.NewMsgTstat(new Tstat(tag, fid)),
                _ => NinePMessage.NewMsgTflush(new Tflush(tag, tag))
            };
        }
    }

    private class NullRemoteMountProvider : IRemoteMountProvider
    {
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath) => Task.FromResult<IBackendRuntime?>(null);
        public void Dispose() { }
    }
}

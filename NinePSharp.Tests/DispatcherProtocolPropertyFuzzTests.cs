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

public class DispatcherProtocolPropertyFuzzTests
{
    private readonly IRemoteMountProvider _cluster;

    public DispatcherProtocolPropertyFuzzTests()
    {
        _cluster = new NullRemoteMountProvider();
    }

    [Property(MaxTest = 100)]
    public bool Dispatcher_Fuzz_ValidMessages_Never_Throws(int seed)
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, _cluster);

        try
        {
            // Note: Many will return Rerror (Unknown FID, etc), but none should throw internal exceptions
            var message = CreateMessage(seed);
            dispatcher.DispatchAsync("session-fuzz", message, NinePDialect.NineP2000).Wait();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static NinePMessage CreateMessage(int seed)
    {
        unchecked
        {
            ushort tag = (ushort)(Math.Abs(seed) % ushort.MaxValue);
            uint fid = (uint)(Math.Abs(seed) % 16);
            ulong offset = (ulong)(Math.Abs(seed) % 128);
            uint count = (uint)(Math.Abs(seed) % 256);
            string name = "m" + Math.Abs(seed % 100).ToString();

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

    [Property(MaxTest = 50)]
    public bool Session_Isolation_Fuzz(NonEmptyString sessionA, NonEmptyString sessionB, uint fid)
    {
        // Skip trivial cases and whitespace-only session IDs (the dispatcher requires valid IDs)
        if (sessionA.Get == sessionB.Get) return true;
        if (string.IsNullOrWhiteSpace(sessionA.Get) || string.IsNullOrWhiteSpace(sessionB.Get)) return true;

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, _cluster);

        // Attach session A
        dispatcher.DispatchAsync(sessionA.Get, NinePMessage.NewMsgTattach(new Tattach(1, fid, uint.MaxValue, "user", "mock")), NinePDialect.NineP2000).Wait();

        // Try reading from session B using same FID
        var res = dispatcher.DispatchAsync(sessionB.Get, NinePMessage.NewMsgTstat(new Tstat(2, fid)), NinePDialect.NineP2000).Result;

        return res is Rerror;
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

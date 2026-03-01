using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Coyote.Specifications;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherRemoveReadRaceCoyoteTests
{
    [Fact]
    public static async Task TestRemoveReadRace()
    {
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.SetupProperty(f => f.Dialect);
        mockFs.Setup(x => x.OpenAsync(It.IsAny<Topen>()))
              .ReturnsAsync(new Ropen(5, new Qid(QidType.QTFILE, 0, 1), 0));
        mockFs.Setup(x => x.ReadAsync(It.IsAny<Tread>()))
              .ReturnsAsync(new Rread(1, new byte[] { 1 }));
        mockFs.Setup(x => x.RemoveAsync(It.IsAny<Tremove>()))
              .ReturnsAsync(new Rremove(1));
        mockFs.Setup(x => x.Clone()).Returns(mockFs.Object);

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(() => RuntimeFileSystemAdapter.ToRuntime(mockFs.Object));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "mock" })), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTopen(new Topen(5, 2, 0)), NinePDialect.NineP2000);

        var t1 = CoyoteTask.Run(async () =>
        {
            await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTread(new Tread(3, 2, 0, 1)), NinePDialect.NineP2000);
        });

        var t2 = CoyoteTask.Run(async () =>
        {
            await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTremove(new Tremove(4, 2)), NinePDialect.NineP2000);
        });

        await CoyoteTask.WhenAll(t1, t2);
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

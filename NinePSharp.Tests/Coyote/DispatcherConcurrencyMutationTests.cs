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

public class DispatcherConcurrencyMutationTests
{
    [Fact]
    public static async Task TestConcurrentFidMutations()
    {
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.SetupProperty(f => f.Dialect);
        mockFs.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
              .ReturnsAsync(new Rwalk(1, new[] { new Qid(QidType.QTDIR, 0, 1) }));
        mockFs.Setup(x => x.Clone()).Returns(mockFs.Object);

        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(() => RuntimeFileSystemAdapter.ToRuntime(mockFs.Object));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")), NinePDialect.NineP2000);

        var t1 = CoyoteTask.Run(async () =>
        {
            await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "mock" })), NinePDialect.NineP2000);
        });

        var t2 = CoyoteTask.Run(async () =>
        {
            await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(3, 1, 3, new[] { "mock" })), NinePDialect.NineP2000);
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

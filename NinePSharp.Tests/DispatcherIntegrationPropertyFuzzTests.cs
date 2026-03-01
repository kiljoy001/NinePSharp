using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

public class DispatcherIntegrationPropertyFuzzTests
{
    [Property(MaxTest = 100)]
    public bool Dispatcher_Union_Read_Is_Stable(string[] branches)
    {
        if (branches == null || branches.Length == 0) return true;
        
        var backends = branches.Select((b, i) =>
        {
            var name = DispatcherIntegrationTestKit.CleanMount(b, i);
            return new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { name }));
        }).ToList();

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(backends);
        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" }).Sync();
        DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101).Sync();

        var read1 = DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 101, 0, 8192).Sync();
        var stats1 = DispatcherIntegrationTestKit.ParseStatsTable(read1.Data.Span);
        
        var read2 = DispatcherIntegrationTestKit.ReadAsync(dispatcher, 5, 101, 0, 8192).Sync();
        var stats2 = DispatcherIntegrationTestKit.ParseStatsTable(read2.Data.Span);

        return stats1.Count == stats2.Count && stats1.SequenceEqual(stats2);
    }

    [Property(MaxTest = 100)]
    public bool Dispatcher_Path_Lookup_Is_Consistent(string[] path)
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        var walk = dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(1, 1, 2, path)), NinePDialect.NineP2000).Result;
        var walk2 = dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTwalk(new Twalk(1, 1, 2, path)), NinePDialect.NineP2000).Result;

        return walk.GetType() == walk2.GetType();
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

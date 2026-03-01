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

public class DispatcherNamespaceDeterminismPropertyFuzzTests
{
    [Property(MaxTest = 100)]
    public bool Dispatcher_Walk_Resolution_Is_Deterministic(string[] path)
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("mock");
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        var res1 = dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Result;
        var res2 = dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "u", "/")), NinePDialect.NineP2000).Result;

        var walk1 = dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, path)), NinePDialect.NineP2000).Result;
        var walk2 = dispatcher.DispatchAsync("s2", NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, path)), NinePDialect.NineP2000).Result;

        if (walk1 is Rwalk r1 && walk2 is Rwalk r2)
        {
            return r1.Wqid.Length == r2.Wqid.Length;
        }
        return walk1.GetType() == walk2.GetType();
    }

    private class NamedStubBackend : IProtocolBackend
    {
        private readonly string _name;
        private readonly string _mountPath;

        public NamedStubBackend(string name, string mountPath)
        {
            _name = name;
            _mountPath = mountPath;
        }

        public string Name => _name;
        public string MountPath => _mountPath;
        public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration c) => Task.CompletedTask;
        public IBackendRuntime GetRuntime(X509Certificate2? c = null) => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem());
        public IBackendRuntime GetRuntime(System.Security.SecureString? s, X509Certificate2? c = null) => GetRuntime(c);
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

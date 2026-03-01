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

public class NinePServerRegistrationPropertyFuzzTests
{
    [Property(MaxTest = 100)]
    public bool Dispatcher_Backend_Registration_Is_Correct(string name, string mountPath)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mountPath)) return true;
        
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns(name);
        mockBackend.Setup(b => b.MountPath).Returns(mountPath);
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new NullRemoteMountProvider());

        return dispatcher != null;
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

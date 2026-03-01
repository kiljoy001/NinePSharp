using NinePSharp.Constants;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FluentAssertions;
using System.Security;
using System.Threading.Tasks;
using System;

namespace NinePSharp.Tests;

public class NinePFSDispatcherTests
{
    private class MockBackend : IProtocolBackend
    {
        public string Name => "Mock";
        public string MountPath => "/mock";
        public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration configuration) => Task.CompletedTask;
        public IBackendRuntime GetRuntime(X509Certificate2? certificate = null) => 
            RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem());
        public IBackendRuntime GetRuntime(System.Security.SecureString? credentials, X509Certificate2? certificate = null) => GetRuntime(certificate);
    }

    [Fact]
    public async Task DispatchAsync_Tattach_Returns_Rattach()
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("test");
        mockBackend.Setup(b => b.MountPath).Returns("/test");
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<X509Certificate2>()))
                   .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MockFileSystem()));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IRemoteMountProvider>().Object);

        var tattach = new Tattach(1, 1, uint.MaxValue, "scott", "test");
        var response = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);

        response.Should().BeOfType<Rattach>();
    }

    [Fact]
    public async Task DispatchAsync_Tversion_Returns_Rversion()
    {
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, Array.Empty<IProtocolBackend>(), new Mock<IRemoteMountProvider>().Object);

        var tversion = new Tversion(1, 8192, "9P2000");
        var response = await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTversion(tversion), NinePDialect.NineP2000);

        response.Should().BeOfType<Rversion>();
        ((Rversion)response).Version.Should().Be("9P2000");
    }

    [Fact]
    public async Task DispatchAsync_Twrite_AuthFid_Populates_SecureString()
    {
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.SetupProperty(f => f.Dialect);
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("test");
        mockBackend.Setup(b => b.MountPath).Returns("/test");
        
        SecureString? capturedCredentials = null;
        mockBackend.Setup(b => b.GetRuntime(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>()))
                   .Callback<SecureString, X509Certificate2>((ss, cert) => capturedCredentials = ss)
                   .Returns(() => RuntimeFileSystemAdapter.ToRuntime(mockFs.Object));

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IRemoteMountProvider>().Object);

        // 1. Tauth
        var tauth = new Tauth(1, 10, "scott", "test");
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);

        // 2. Twrite to auth FID
        string secret = "rpcuser:rpcpass";
        var twrite = new Twrite(2, 10, 0, System.Text.Encoding.UTF8.GetBytes(secret));
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000);

        // 3. Tattach referencing auth FID
        var tattach = new Tattach(3, 1, 10, "scott", "test");
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s1", NinePMessage.NewMsgTstat(new Tstat(4, 1)), NinePDialect.NineP2000);

        capturedCredentials.Should().NotBeNull();
        
        IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(capturedCredentials!);
        try {
            string? recovered = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
            recovered.Should().NotBeNull();
            recovered.Should().Be(secret);
        }
        finally {
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}

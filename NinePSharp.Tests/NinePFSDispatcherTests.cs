using NinePSharp.Constants;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using System;
using System.Text;

namespace NinePSharp.Tests;

public class NinePFSDispatcherTests
{
    private static IRemoteMountProvider CreateRemoteMountProvider() => new NullRemoteMountProvider();

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private class MockBackend : IProtocolBackend
    {
        public string Name => "Mock";
        public string MountPath => "/mock";
        public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration configuration) => Task.CompletedTask;
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new MockFileSystem();
        public INinePFileSystem GetFileSystem(System.Security.SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
    }

    [Fact]
    public async Task DispatchAsync_Tversion_Returns_Rversion()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateRemoteMountProvider());
        var tversion = new Tversion(1, 8192, "9P2000");
        var message = NinePMessage.NewMsgTversion(tversion);

        // Act
        var response = await dispatcher.DispatchAsync(message, NinePDialect.NineP2000);

        // Assert
        response.Should().BeOfType<Rversion>();
        var rversion = (Rversion)response;
        rversion.Tag.Should().Be(1);
        rversion.MSize.Should().Be(8192);
        rversion.Version.Should().Be("9P2000");
    }

    [Fact]
    public async Task DispatchAsync_Tattach_With_No_Backend_Returns_Rerror()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateRemoteMountProvider());
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "notfound");
        var message = NinePMessage.NewMsgTattach(tattach);

        // Act
        var response = await dispatcher.DispatchAsync(message, NinePDialect.NineP2000);

        // Assert — no backends registered, so dispatcher returns Rerror
        response.Should().BeOfType<Rerror>();
        ((Rerror)response).Ename.Should().Contain("No backend");
    }

    [Fact]
    public async Task DispatchAsync_Tclunk_Removes_Fid()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateRemoteMountProvider());
        
        // First attach to create a FID
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);

        var tclunk = new Tclunk(2, 100);
        var message = NinePMessage.NewMsgTclunk(tclunk);

        // Act
        var response = await dispatcher.DispatchAsync(message, NinePDialect.NineP2000);

        // Assert
        response.Should().BeOfType<Rclunk>();
    }

    [Fact]
    public async Task DispatchAsync_Tflush_Returns_Rflush()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateRemoteMountProvider());
        var tflush = new Tflush(1, 1);
        var message = NinePMessage.NewMsgTflush(tflush);

        // Act
        var response = await dispatcher.DispatchAsync(message, NinePDialect.NineP2000);

        // Assert — Tflush is now handled
        response.Should().BeOfType<Rflush>();
    }

    [Fact]
    public async Task DispatchAsync_Twrite_AuthFid_Populates_SecureString()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.Name).Returns("test");
        mockBackend.Setup(b => b.MountPath).Returns("/test");
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.SetupProperty(f => f.Dialect);
        mockFs.Setup(f => f.ReadAsync(It.IsAny<Tread>()))
            .ReturnsAsync((Tread t) => new Rread(t.Tag, Array.Empty<byte>()));
        
        SecureString? capturedCredentials = null;
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>()))
                   .Callback<SecureString, X509Certificate2>((ss, cert) => capturedCredentials = ss)
                   .Returns(mockFs.Object);

        var dispatcher = new NinePFSDispatcher(logger, new[] { mockBackend.Object }, CreateRemoteMountProvider());

        uint authFid = 100;
        ushort tag = 1;

        // 1. Tauth to create the auth fid
        var tauth = new Tauth(tag, authFid, "user", "test");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);

        // 2. Twrite to the auth fid with a secret
        string secret = "P@ssw0rd123";
        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        var twrite = new Twrite(tag, authFid, 0, secretBytes);
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000);

        // 3. Tattach to consume the secret
        var tattach = new Tattach(tag, 200, authFid, "scott", "test");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTread(new Tread((ushort)(tag + 1), 200, 0, 1)), NinePDialect.NineP2000);

        // Assert
        capturedCredentials.Should().NotBeNull();
        capturedCredentials!.Length.Should().Be(secret.Length);
        
        // Final verification of content via Scoped Reveal logic
        IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(capturedCredentials);
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

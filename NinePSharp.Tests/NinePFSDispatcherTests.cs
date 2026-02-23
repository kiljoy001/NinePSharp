using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Constants;
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
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    private ClusterManager CreateMockClusterManager()
    {
        var logger = new Mock<ILogger<ClusterManager>>();
        var factory = new Mock<ILoggerFactory>();
        var config = new ServerConfig(); 
        return new ClusterManager(logger.Object, factory.Object, config);
    }

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
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new NinePSharp.Server.Backends.MockFileSystem(_vault);
        public INinePFileSystem GetFileSystem(System.Security.SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
    }

    [Fact]
    public async Task DispatchAsync_Tversion_Returns_Rversion()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateMockClusterManager());
        var tversion = new Tversion(1, 8192, "9P2000");
        var message = NinePMessage.NewMsgTversion(tversion);

        // Act
        var response = await dispatcher.DispatchAsync(message, false);

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
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateMockClusterManager());
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "none");
        var message = NinePMessage.NewMsgTattach(tattach);

        // Act
        var response = await dispatcher.DispatchAsync(message, false);

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
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateMockClusterManager());
        
        // First attach to create a FID
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "none");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(tattach), false);

        var tclunk = new Tclunk(2, 100);
        var message = NinePMessage.NewMsgTclunk(tclunk);

        // Act
        var response = await dispatcher.DispatchAsync(message, false);

        // Assert
        response.Should().BeOfType<Rclunk>();
    }

    [Fact]
    public async Task DispatchAsync_Tflush_Returns_Rflush()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends, CreateMockClusterManager());
        var tflush = new Tflush(1, 1);
        var message = NinePMessage.NewMsgTflush(tflush);

        // Act
        var response = await dispatcher.DispatchAsync(message, false);

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
        
        SecureString? capturedCredentials = null;
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>()))
                   .Callback<SecureString, X509Certificate2>((ss, cert) => capturedCredentials = ss)
                   .Returns(new Mock<INinePFileSystem>().Object);

        var dispatcher = new NinePFSDispatcher(logger, new[] { mockBackend.Object }, CreateMockClusterManager());

        uint authFid = 100;
        ushort tag = 1;

        // 1. Tauth to create the auth fid
        var tauth = new Tauth(tag, authFid, "user", "test");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTauth(tauth), false);

        // 2. Twrite to the auth fid with a secret
        string secret = "P@ssw0rd123";
        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        var twrite = new Twrite(tag, authFid, 0, secretBytes);
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), false);

        // 3. Tattach to consume the secret
        var tattach = new Tattach(tag, 200, authFid, "scott", "test");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(tattach), false);

        // Assert
        capturedCredentials.Should().NotBeNull();
        capturedCredentials!.Length.Should().Be(secret.Length);
        
        // Final verification of content via Scoped Reveal logic
        IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(capturedCredentials);
        try {
            string recovered = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
            recovered.Should().Be(secret);
        }
        finally {
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
    [Fact]
    public async Task RootFileSystem_StatfsAsync_Returns_Dummy_Stats()
    {
        // Arrange
        var backends = new List<IProtocolBackend>();
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());
        var tstatfs = new Tstatfs(1, 100, 1);

        // Act
        var result = await rootFs.StatfsAsync(tstatfs);

        // Assert
        result.Should().BeOfType<Rstatfs>();
        result.Tag.Should().Be(tstatfs.Tag);
        result.BSize.Should().Be(4096);
        result.Blocks.Should().Be(1000000);
        result.BFree.Should().Be(500000);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Returns_Mounted_Backends()
    {
        // Arrange
        var mockBackend1 = new Mock<IProtocolBackend>();
        mockBackend1.Setup(b => b.MountPath).Returns("/secrets");
        var mockBackend2 = new Mock<IProtocolBackend>();
        mockBackend2.Setup(b => b.MountPath).Returns("/mock");

        var backends = new List<IProtocolBackend> { mockBackend1.Object, mockBackend2.Object };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());
        
        // Act - Offset 0 should return all mounts
        var treaddir = new Treaddir(1, 100, 1, 0, 8192);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);
        result.Data.Length.Should().Be((int)result.Count);
        
        // Verify it contains our backend names
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("mock");
        dataString.Should().Contain("secrets");
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_With_Offset_Returns_Empty()
    {
        // Arrange
        var backends = new List<IProtocolBackend>();
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act - Offset > 0 returns empty for simplicity in the root
        var treaddir = new Treaddir(1, 100, 1, 1, 8192);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().Be(0);
        result.Data.Length.Should().Be(0);
    }

    [Fact]
    public async Task RootFileSystem_StatfsAsync_Delegates_When_Entered_Backend()
    {
        // Arrange
        var mockBackend = new Mock<IProtocolBackend>();
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.Setup(f => f.StatfsAsync(It.IsAny<Tstatfs>()))
              .ReturnsAsync(new Rstatfs(1, 0x12345678, 8192, 2000000, 1000000, 1000000, 5000, 2500, 42, 512));
        mockBackend.Setup(b => b.MountPath).Returns("/test");
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);

        var backends = new List<IProtocolBackend> { mockBackend.Object };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Walk into the backend
        await rootFs.WalkAsync(new Twalk(1, 100, 101, new[] { "test" }));

        // Act - StatfsAsync should now delegate to the backend
        var tstatfs = new Tstatfs(2, 101, 2);
        var result = await rootFs.StatfsAsync(tstatfs);

        // Assert - should get backend's custom stats, not root's defaults
        result.BSize.Should().Be(8192);
        result.Blocks.Should().Be(2000000);
        result.FsType.Should().Be(0x12345678);
        mockFs.Verify(f => f.StatfsAsync(It.IsAny<Tstatfs>()), Times.Once);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Delegates_When_Entered_Backend()
    {
        // Arrange
        var mockBackend = new Mock<IProtocolBackend>();
        var mockFs = new Mock<INinePFileSystem>();

        // Create custom readdir response
        var customData = Encoding.UTF8.GetBytes("backend_readdir_data");
        mockFs.Setup(f => f.ReaddirAsync(It.IsAny<Treaddir>()))
              .ReturnsAsync(new Rreaddir((uint)(customData.Length + 11), 1, (uint)customData.Length, customData));

        mockBackend.Setup(b => b.MountPath).Returns("/test");
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(mockFs.Object);

        var backends = new List<IProtocolBackend> { mockBackend.Object };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Walk into the backend
        await rootFs.WalkAsync(new Twalk(1, 100, 101, new[] { "test" }));

        // Act - ReaddirAsync should now delegate to the backend
        var treaddir = new Treaddir(2, 101, 2, 0, 8192);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert
        result.Data.ToArray().Should().Equal(customData);
        mockFs.Verify(f => f.ReaddirAsync(It.IsAny<Treaddir>()), Times.Once);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Handles_100_Backends()
    {
        // Arrange - Create 100 backends
        var backends = new List<IProtocolBackend>();
        for (int i = 0; i < 100; i++)
        {
            var mockBackend = new Mock<IProtocolBackend>();
            mockBackend.Setup(b => b.MountPath).Returns($"/backend{i:D3}");
            backends.Add(mockBackend.Object);
        }

        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act
        var treaddir = new Treaddir(1, 100, 1, 0, 65536); // Large count to get all entries
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);

        // Verify all backends are listed
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        for (int i = 0; i < 100; i++)
        {
            dataString.Should().Contain($"backend{i:D3}");
        }
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_With_Long_Backend_Names()
    {
        // Arrange - Create backends with very long names
        var backends = new List<IProtocolBackend>();
        var longName1 = new string('a', 200);
        var longName2 = new string('b', 200);

        var mockBackend1 = new Mock<IProtocolBackend>();
        mockBackend1.Setup(b => b.MountPath).Returns("/" + longName1);
        var mockBackend2 = new Mock<IProtocolBackend>();
        mockBackend2.Setup(b => b.MountPath).Returns("/" + longName2);

        backends.Add(mockBackend1.Object);
        backends.Add(mockBackend2.Object);

        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act
        var treaddir = new Treaddir(1, 100, 1, 0, 65536);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);

        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain(longName1);
        dataString.Should().Contain(longName2);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Respects_Count_Limit()
    {
        // Arrange - Create many backends but use small count
        var backends = new List<IProtocolBackend>();
        for (int i = 0; i < 50; i++)
        {
            var mockBackend = new Mock<IProtocolBackend>();
            mockBackend.Setup(b => b.MountPath).Returns($"/backend{i:D2}");
            backends.Add(mockBackend.Object);
        }

        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act - Request only 512 bytes
        var treaddir = new Treaddir(1, 100, 1, 0, 512);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert - Should not exceed requested count
        result.Count.Should().BeLessThanOrEqualTo(512);
        result.Data.Length.Should().Be((int)result.Count);
    }

    [Fact]
    public async Task RootFileSystem_Walk_From_Root_To_MockBackend()
    {
        // Arrange
        var mockBackend = new MockBackend();
        var backends = new List<IProtocolBackend> { mockBackend };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act - Walk to /mock
        var twalk = new Twalk(1, 100, 101, new[] { "mock" });
        var result = await rootFs.WalkAsync(twalk);

        // Assert
        result.Should().BeOfType<Rwalk>();
        result.Wqid.Should().HaveCount(1);
        result.Wqid[0].Type.Should().Be(QidType.QTDIR);
    }

    [Fact]
    public async Task RootFileSystem_StatfsAsync_Returns_Consistent_Values()
    {
        // Arrange
        var backends = new List<IProtocolBackend>();
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act - Call multiple times
        var result1 = await rootFs.StatfsAsync(new Tstatfs(1, 100, 1));
        var result2 = await rootFs.StatfsAsync(new Tstatfs(2, 100, 2));

        // Assert - Should return consistent values
        result1.BSize.Should().Be(result2.BSize);
        result1.Blocks.Should().Be(result2.Blocks);
        result1.BFree.Should().Be(result2.BFree);
        result1.FsType.Should().Be(result2.FsType);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Returns_Sorted_Backends()
    {
        // Arrange - Create backends in non-alphabetical order
        var mockBackend1 = new Mock<IProtocolBackend>();
        mockBackend1.Setup(b => b.MountPath).Returns("/zebra");
        var mockBackend2 = new Mock<IProtocolBackend>();
        mockBackend2.Setup(b => b.MountPath).Returns("/alpha");
        var mockBackend3 = new Mock<IProtocolBackend>();
        mockBackend3.Setup(b => b.MountPath).Returns("/beta");

        var backends = new List<IProtocolBackend> { mockBackend1.Object, mockBackend2.Object, mockBackend3.Object };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act
        var treaddir = new Treaddir(1, 100, 1, 0, 8192);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert - Should be alphabetically sorted
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        int alphaPos = dataString.IndexOf("alpha");
        int betaPos = dataString.IndexOf("beta");
        int zebraPos = dataString.IndexOf("zebra");

        alphaPos.Should().BeLessThan(betaPos);
        betaPos.Should().BeLessThan(zebraPos);
    }

    [Fact]
    public async Task RootFileSystem_ReaddirAsync_Filters_Empty_Mount_Paths()
    {
        // Arrange - Create backend with empty mount path
        var mockBackend1 = new Mock<IProtocolBackend>();
        mockBackend1.Setup(b => b.MountPath).Returns("");
        var mockBackend2 = new Mock<IProtocolBackend>();
        mockBackend2.Setup(b => b.MountPath).Returns("/valid");

        var backends = new List<IProtocolBackend> { mockBackend1.Object, mockBackend2.Object };
        var rootFs = new RootFileSystem(backends, CreateMockClusterManager());

        // Act
        var treaddir = new Treaddir(1, 100, 1, 0, 8192);
        var result = await rootFs.ReaddirAsync(treaddir);

        // Assert - Should only contain 'valid', not empty path
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("valid");
        dataString.Should().NotContain("\"\"");
    }
}

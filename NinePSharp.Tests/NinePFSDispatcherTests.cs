using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Backends;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

public class NinePFSDispatcherTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

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
        public INinePFileSystem GetFileSystem() => new NinePSharp.Server.Backends.MockFileSystem(_vault);
        public INinePFileSystem GetFileSystem(System.Security.SecureString? credentials) => GetFileSystem();
    }

    [Fact]
    public async Task DispatchAsync_Tversion_Returns_Rversion()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends);
        var tversion = new Tversion(1, 8192, "9P2000");
        var message = NinePMessage.NewMsgTversion(tversion);

        // Act
        var response = await dispatcher.DispatchAsync(message);

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
        var dispatcher = new NinePFSDispatcher(logger, backends);
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "none");
        var message = NinePMessage.NewMsgTattach(tattach);

        // Act
        var response = await dispatcher.DispatchAsync(message);

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
        var dispatcher = new NinePFSDispatcher(logger, backends);
        
        // First attach to create a FID
        var tattach = new Tattach(1, 100, uint.MaxValue, "root", "none");
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(tattach));

        var tclunk = new Tclunk(2, 100);
        var message = NinePMessage.NewMsgTclunk(tclunk);

        // Act
        var response = await dispatcher.DispatchAsync(message);

        // Assert
        response.Should().BeOfType<Rclunk>();
    }

    [Fact]
    public async Task DispatchAsync_Tflush_Returns_Rflush()
    {
        // Arrange
        var logger = new TestLogger<NinePFSDispatcher>();
        var backends = new List<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(logger, backends);
        var tflush = new Tflush(1, 1);
        var message = NinePMessage.NewMsgTflush(tflush);

        // Act
        var response = await dispatcher.DispatchAsync(message);

        // Assert — Tflush is now handled
        response.Should().BeOfType<Rflush>();
    }
}

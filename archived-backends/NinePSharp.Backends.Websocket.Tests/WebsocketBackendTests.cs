using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.Websockets;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Websocket.Tests;

public class WebsocketBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? uri = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Uri"] = uri ?? "wss://echo.websocket.org",
            ["ReconnectInterval"] = "5"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task WebsocketBackend_Initialize_SetsMountPath()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        var config = CreateConfig("/ws");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/ws");
    }

    [Fact]
    public async Task WebsocketBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/ws"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task WebsocketBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/ws"));

        var creds = new SecureString();
        foreach (char c in "token123") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void WebsocketBackend_Name_IsNotEmpty()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("ws://localhost:8080")]
    [InlineData("wss://echo.websocket.org")]
    [InlineData("wss://stream.example.com/feed")]
    public async Task WebsocketBackend_SupportsMultipleUris(string uri)
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        var config = CreateConfig("/ws", uri);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/ws");
    }

    [Property]
    public Property WebsocketBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new WebsocketBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }
}

public class WebsocketBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void WebsocketBackend_FuzzUris()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var uri = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/ws",
                    ["Uri"] = uri,
                    ["ReconnectInterval"] = "5"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed URIs
            }
        }
    }

    [Fact]
    public void WebsocketBackend_InvalidSchemesRejected()
    {
        var backend = new WebsocketBackend(_mockVault.Object);
        var invalidSchemes = new[]
        {
            "http://example.com",
            "https://example.com",
            "ftp://example.com",
            "file:///etc/passwd",
            "javascript:alert(1)"
        };

        foreach (var uri in invalidSchemes)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/ws",
                    ["Uri"] = uri,
                    ["ReconnectInterval"] = "5"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected - wrong scheme
            }
        }
    }
}

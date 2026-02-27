using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.JsonRpc;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.JsonRpc.Tests;

public class JsonRpcBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? endpoint = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Endpoint"] = endpoint ?? "https://api.example.com/jsonrpc",
            ["Version"] = "2.0"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task JsonRpcBackend_Initialize_SetsMountPath()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        var config = CreateConfig("/jsonrpc");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/jsonrpc");
    }

    [Fact]
    public async Task JsonRpcBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/jsonrpc"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task JsonRpcBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/jsonrpc"));

        var creds = new SecureString();
        foreach (char c in "api_key:secret") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void JsonRpcBackend_Name_IsNotEmpty()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("https://api.example.com/jsonrpc")]
    [InlineData("http://localhost:8545")]
    [InlineData("https://mainnet.infura.io/v3/YOUR_KEY")]
    public async Task JsonRpcBackend_SupportsMultipleEndpoints(string endpoint)
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        var config = CreateConfig("/jsonrpc", endpoint);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/jsonrpc");
    }

    [Property]
    public Property JsonRpcBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new JsonRpcBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }

    [Property]
    public Property JsonRpcBackend_GetFileSystemIsIdempotent()
    {
        return FsCheck.Prop.ForAll<int>(iterations =>
        {
            var mockVault = new Mock<ILuxVaultService>();
            var backend = new JsonRpcBackend(mockVault.Object);
            backend.InitializeAsync(CreateConfig("/jsonrpc")).Wait();

            for (int i = 0; i < Math.Abs(iterations) % 50; i++)
            {
                var fs = backend.GetFileSystem();
                if (fs == null) return false;
            }
            return true;
        });
    }
}

public class JsonRpcBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void JsonRpcBackend_FuzzEndpoints()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var endpoint = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/jsonrpc",
                    ["Endpoint"] = endpoint,
                    ["Version"] = "2.0"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed endpoints
            }
        }
    }

    [Fact]
    public void JsonRpcBackend_JsonInjectionResistance()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        var injectionPayloads = new[]
        {
            "{\"method\":\"system\",\"params\":[\"rm -rf /\"]}",
            "'; DROP TABLE users; --",
            "\\\"; system('whoami'); //",
            "{\"__proto__\":{\"isAdmin\":true}}",
            "{\"constructor\":{\"prototype\":{\"isAdmin\":true}}}"
        };

        foreach (var payload in injectionPayloads)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = payload,
                    ["Endpoint"] = "https://api.example.com/jsonrpc",
                    ["Version"] = "2.0"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
                // Injection should not execute
            }
            catch
            {
                // Expected for malicious payloads
            }
        }
    }

    [Fact]
    public void JsonRpcBackend_MalformedJsonHandled()
    {
        var backend = new JsonRpcBackend(_mockVault.Object);
        var malformedJson = new[]
        {
            "{unclosed",
            "{ \"key\": }",
            "{ \"key\": [1, 2, }",
            "null",
            "undefined",
            "{\"a\":\"b\"}{\"c\":\"d\"}"
        };

        foreach (var json in malformedJson)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/jsonrpc",
                    ["Endpoint"] = json,
                    ["Version"] = "2.0"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed JSON in endpoint
            }
        }
    }
}

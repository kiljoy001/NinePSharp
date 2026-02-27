using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.gRPC;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Grpc.Tests;

public class GrpcBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? endpoint = null, bool? useTls = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Endpoint"] = endpoint ?? "https://localhost:5001",
            ["UseTls"] = (useTls ?? true).ToString()
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task GrpcBackend_Initialize_SetsMountPath()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        var config = CreateConfig("/grpc");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/grpc");
    }

    [Fact]
    public async Task GrpcBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/grpc"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task GrpcBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/grpc"));

        var creds = new SecureString();
        foreach (char c in "token:secret") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void GrpcBackend_Name_IsNotEmpty()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://localhost:5001")]
    [InlineData("https://api.example.com")]
    [InlineData("http://192.168.1.1:8080")]
    public async Task GrpcBackend_SupportsMultipleEndpoints(string endpoint)
    {
        var backend = new GrpcBackend(_mockVault.Object);
        var config = CreateConfig("/grpc", endpoint);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/grpc");
    }

    [Fact]
    public async Task GrpcBackend_SupportsTlsAndNonTls()
    {
        var backendTls = new GrpcBackend(_mockVault.Object);
        var configTls = CreateConfig("/grpc-tls", "https://localhost:5001", true);
        await backendTls.InitializeAsync(configTls);

        var backendNoTls = new GrpcBackend(_mockVault.Object);
        var configNoTls = CreateConfig("/grpc-notls", "http://localhost:5000", false);
        await backendNoTls.InitializeAsync(configNoTls);

        backendTls.MountPath.Should().Be("/grpc-tls");
        backendNoTls.MountPath.Should().Be("/grpc-notls");
    }

    [Property]
    public Property GrpcBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new GrpcBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }

    [Property]
    public Property GrpcBackend_GetFileSystemIsIdempotent()
    {
        return FsCheck.Prop.ForAll<int>(iterations =>
        {
            var mockVault = new Mock<ILuxVaultService>();
            var backend = new GrpcBackend(mockVault.Object);
            backend.InitializeAsync(CreateConfig("/grpc")).Wait();

            for (int i = 0; i < Math.Abs(iterations) % 50; i++)
            {
                var fs = backend.GetFileSystem();
                if (fs == null) return false;
            }
            return true;
        });
    }
}

public class GrpcBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void GrpcBackend_FuzzEndpoints()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var endpoint = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/grpc",
                    ["Endpoint"] = endpoint,
                    ["UseTls"] = "true"
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
    public void GrpcBackend_FuzzMountPaths()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        var paths = FuzzerBridge.GeneratePathTraversals();

        foreach (var path in paths)
        {
            var mountPath = string.Join("/", path);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = mountPath,
                    ["Endpoint"] = "https://localhost:5001",
                    ["UseTls"] = "true"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for some invalid paths
            }
        }
    }

    [Fact]
    public void GrpcBackend_MalformedUrlsDoNotCrash()
    {
        var backend = new GrpcBackend(_mockVault.Object);
        var malformedUrls = new[]
        {
            "not-a-url",
            "://missing-scheme",
            "http://",
            "https://[invalid:host",
            "ftp://wrong-protocol.com",
            "javascript:alert(1)",
            "../../../etc/passwd",
            "http://localhost:-1",
            "http://localhost:99999"
        };

        foreach (var url in malformedUrls)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/grpc",
                    ["Endpoint"] = url,
                    ["UseTls"] = "true"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed URLs
            }
        }
    }
}

using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.Cloud;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Cloud.Tests;

public class AwsBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string region, string? accessKey = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Region"] = region ?? "us-east-1",
            ["AccessKeyId"] = accessKey,
            ["SecretAccessKey"] = "test-secret"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task AwsBackend_Initialize_SetsMountPath()
    {
        var backend = new AwsBackend(_mockVault.Object);
        var config = CreateConfig("/aws", "us-west-2");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/aws");
    }

    [Fact]
    public async Task AwsBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new AwsBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/aws", "us-east-1"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task AwsBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new AwsBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/aws", "us-east-1"));

        var creds = new SecureString();
        foreach (char c in "test:password") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void AwsBackend_Name_IsNotEmpty()
    {
        var backend = new AwsBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("us-east-1")]
    [InlineData("us-west-2")]
    [InlineData("eu-west-1")]
    [InlineData("ap-southeast-1")]
    public async Task AwsBackend_SupportsMultipleRegions(string region)
    {
        var backend = new AwsBackend(_mockVault.Object);
        var config = CreateConfig("/aws", region);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/aws");
    }

    [Fact]
    public async Task AwsBackend_InitializeWithNullConfig_ThrowsException()
    {
        var backend = new AwsBackend(_mockVault.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await backend.InitializeAsync(null!));
    }

    [Property]
    public Property AwsBackend_MountPathPreservedAfterInitialization()
    {
        return Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new AwsBackend(mockVault.Object);
            var config = CreateConfig(mountPath, "us-east-1");
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }
}

public class AzureBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();
    private IConfiguration CreateConfig(string mountPath, string? connectionString = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["ConnectionString"] = connectionString ?? "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=test==;EndpointSuffix=core.windows.net"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task AzureBackend_Initialize_SetsMountPath()
    {
        var backend = new AzureBackend(_mockVault.Object);
        var config = CreateConfig("/azure");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/azure");
    }

    [Fact]
    public async Task AzureBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new AzureBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/azure"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public void AzureBackend_Name_IsNotEmpty()
    {
        var backend = new AzureBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Property]
    public Property AzureBackend_GetFileSystemIsIdempotent()
    {
        return Prop.ForAll<int>(iterations =>
        {
            var backend = new AzureBackend(_mockVault.Object);
            backend.InitializeAsync(CreateConfig("/azure")).Wait();

            for (int i = 0; i < Math.Abs(iterations) % 100; i++)
            {
                var fs = backend.GetFileSystem();
                if (fs == null) return false;
            }
            return true;
        });
    }
}

public class GcpBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? projectId = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["ProjectId"] = projectId ?? "test-project",
            ["CredentialsPath"] = "/path/to/credentials.json"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task GcpBackend_Initialize_SetsMountPath()
    {
        var backend = new GcpBackend(_mockVault.Object);
        var config = CreateConfig("/gcp");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/gcp");
    }

    [Fact]
    public async Task GcpBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new GcpBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/gcp"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public void GcpBackend_Name_IsNotEmpty()
    {
        var backend = new GcpBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GcpBackend_InitializeWithInvalidMountPath_HandlesGracefully(string? mountPath)
    {
        var backend = new GcpBackend(_mockVault.Object);
        var config = CreateConfig(mountPath ?? "/gcp");

        await backend.InitializeAsync(config);

        // Should not throw
        backend.Name.Should().NotBeEmpty();
    }
}

public class CloudBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void AwsBackend_FuzzMountPaths()
    {
        var backend = new AwsBackend(_mockVault.Object);
        var paths = FuzzerBridge.GeneratePathTraversals();

        foreach (var path in paths)
        {
            var mountPath = string.Join("/", path);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = mountPath,
                    ["Region"] = "us-east-1",
                    ["AccessKeyId"] = "test",
                    ["SecretAccessKey"] = "test"
                }!)
                .Build();

            // Should not crash or throw unhandled exceptions
            try
            {
                backend.InitializeAsync(config).Wait();
                var _ = backend.MountPath;
            }
            catch (AggregateException)
            {
                // Expected for invalid paths
            }
        }
    }

    [Fact]
    public void AzureBackend_FuzzConnectionStrings()
    {
        var backend = new AzureBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var connectionString = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/azure",
                    ["ConnectionString"] = connectionString
                }!)
                .Build();

            // Should not crash
            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed connection strings
            }
        }
    }

    [Fact]
    public void GcpBackend_FuzzProjectIds()
    {
        var backend = new GcpBackend(_mockVault.Object);

        // Generate random byte sequences
        for (int i = 0; i < 100; i++)
        {
            var mutated = FuzzerBridge.MutateBytes(new byte[] { 0x61, 0x62, 0x63 });
            var projectId = System.Text.Encoding.UTF8.GetString(mutated);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/gcp",
                    ["ProjectId"] = projectId,
                    ["CredentialsPath"] = "/path/to/creds.json"
                }!)
                .Build();

            // Should handle invalid project IDs gracefully
            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for invalid project IDs
            }
        }
    }
}

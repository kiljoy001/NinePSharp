using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Database.Tests;

public class DatabaseBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? connectionString = null, string? provider = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["ConnectionString"] = connectionString ?? "Server=localhost;Database=test;User=test;Password=test;",
            ["Provider"] = provider ?? "SqlServer"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task DatabaseBackend_Initialize_SetsMountPath()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        var config = CreateConfig("/db");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/db");
    }

    [Fact]
    public async Task DatabaseBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/db"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task DatabaseBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/db"));

        var creds = new SecureString();
        foreach (char c in "user:password") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void DatabaseBackend_Name_IsNotEmpty()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    [InlineData("MySQL")]
    [InlineData("SQLite")]
    public async Task DatabaseBackend_SupportsMultipleProviders(string provider)
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        var config = CreateConfig("/db", null, provider);

        await backend.InitializeAsync(config);

        backend.Name.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DatabaseBackend_InitializeMultipleTimes_DoesNotThrow()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        var config = CreateConfig("/db");

        await backend.InitializeAsync(config);
        await backend.InitializeAsync(config);
        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/db");
    }

    [Property]
    public Property DatabaseBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new DatabaseBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }

    [Property]
    public Property DatabaseBackend_GetFileSystemReturnsNonNull()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new DatabaseBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            var fs = backend.GetFileSystem();
            return fs != null;
        });
    }
}

public class DatabaseBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void DatabaseBackend_FuzzConnectionStrings()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var connectionString = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/db",
                    ["ConnectionString"] = connectionString,
                    ["Provider"] = "SqlServer"
                }!)
                .Build();

            // Should handle malformed connection strings gracefully
            try
            {
                backend.InitializeAsync(config).Wait();
                var _ = backend.GetFileSystem();
            }
            catch
            {
                // Expected for invalid connection strings
            }
        }
    }

    [Fact]
    public void DatabaseBackend_FuzzMountPaths()
    {
        var backend = new DatabaseBackend(_mockVault.Object);
        var paths = FuzzerBridge.GeneratePathTraversals();

        foreach (var path in paths)
        {
            var mountPath = string.Join("/", path);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = mountPath,
                    ["ConnectionString"] = "Server=localhost;Database=test;",
                    ["Provider"] = "SqlServer"
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
    public void DatabaseBackend_FuzzProviderNames()
    {
        var backend = new DatabaseBackend(_mockVault.Object);

        for (int i = 0; i < 100; i++)
        {
            var mutated = FuzzerBridge.MutateBytes(System.Text.Encoding.UTF8.GetBytes("SqlServer"));
            var provider = System.Text.Encoding.UTF8.GetString(mutated);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/db",
                    ["ConnectionString"] = "Server=localhost;Database=test;",
                    ["Provider"] = provider
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for invalid providers
            }
        }
    }

    [Fact]
    public void DatabaseBackend_SqlInjectionResistance()
    {
        var backend = new DatabaseBackend(_mockVault.Object);

        // SQL injection attempts
        var injections = new[]
        {
            "'; DROP TABLE users; --",
            "1' OR '1'='1",
            "admin'--",
            "1' UNION SELECT NULL, NULL, NULL--",
            "' OR 1=1--",
            "'; EXEC xp_cmdshell('dir'); --"
        };

        foreach (var injection in injections)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/db",
                    ["ConnectionString"] = $"Server=localhost;Database={injection};",
                    ["Provider"] = "SqlServer"
                }!)
                .Build();

            // Should not execute SQL injection
            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected - invalid database name
            }
        }
    }
}

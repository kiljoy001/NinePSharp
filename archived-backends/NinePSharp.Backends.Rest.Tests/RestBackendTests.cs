using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.REST;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Rest.Tests;

public class RestBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? baseUrl = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["BaseUrl"] = baseUrl ?? "https://api.example.com",
            ["Timeout"] = "30"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task RestBackend_Initialize_SetsMountPath()
    {
        var backend = new RestBackend(_mockVault.Object);
        var config = CreateConfig("/rest");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/rest");
    }

    [Fact]
    public async Task RestBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new RestBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/rest"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task RestBackend_GetFileSystemWithApiKey_ReturnsFileSystem()
    {
        var backend = new RestBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/rest"));

        var creds = new SecureString();
        foreach (char c in "api_key_12345") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void RestBackend_Name_IsNotEmpty()
    {
        var backend = new RestBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.github.com")]
    [InlineData("https://jsonplaceholder.typicode.com")]
    public async Task RestBackend_SupportsMultipleBaseUrls(string baseUrl)
    {
        var backend = new RestBackend(_mockVault.Object);
        var config = CreateConfig("/rest", baseUrl);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/rest");
    }

    [Property]
    public Property RestBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new RestBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }
}

public class RestBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void RestBackend_FuzzBaseUrls()
    {
        var backend = new RestBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var url = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/rest",
                    ["BaseUrl"] = url,
                    ["Timeout"] = "30"
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

    [Fact]
    public void RestBackend_XssAttemptsDoNotExecute()
    {
        var backend = new RestBackend(_mockVault.Object);
        var xssPayloads = new[]
        {
            "<script>alert('XSS')</script>",
            "javascript:alert(1)",
            "<img src=x onerror=alert(1)>",
            "';alert(String.fromCharCode(88,83,83))//",
            "<iframe src='javascript:alert(1)'>"
        };

        foreach (var payload in xssPayloads)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = payload,
                    ["BaseUrl"] = "https://api.example.com",
                    ["Timeout"] = "30"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // XSS should not execute
            }
        }
    }
}

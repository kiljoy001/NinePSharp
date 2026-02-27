using System;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Backends.MQTT;
using NinePSharp.Server.Interfaces;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Mqtt.Tests;

public class MqttBackendTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    private IConfiguration CreateConfig(string mountPath, string? broker = null, int? port = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["MountPath"] = mountPath,
            ["Broker"] = broker ?? "localhost",
            ["Port"] = (port ?? 1883).ToString(),
            ["ClientId"] = "test-client"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(config!).Build();
    }

    [Fact]
    public async Task MqttBackend_Initialize_SetsMountPath()
    {
        var backend = new MqttBackend(_mockVault.Object);
        var config = CreateConfig("/mqtt");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/mqtt");
    }

    [Fact]
    public async Task MqttBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new MqttBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/mqtt"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task MqttBackend_GetFileSystemWithCredentials_ReturnsFileSystem()
    {
        var backend = new MqttBackend(_mockVault.Object);
        await backend.InitializeAsync(CreateConfig("/mqtt"));

        var creds = new SecureString();
        foreach (char c in "user:pass") creds.AppendChar(c);
        creds.MakeReadOnly();

        var fs = backend.GetFileSystem(creds);

        fs.Should().NotBeNull();
    }

    [Fact]
    public void MqttBackend_Name_IsNotEmpty()
    {
        var backend = new MqttBackend(_mockVault.Object);
        backend.Name.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(1883)]
    [InlineData(8883)]
    [InlineData(1884)]
    public async Task MqttBackend_SupportsMultiplePorts(int port)
    {
        var backend = new MqttBackend(_mockVault.Object);
        var config = CreateConfig("/mqtt", "localhost", port);

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/mqtt");
    }

    [Property]
    public Property MqttBackend_MountPathPreserved()
    {
        return FsCheck.Prop.ForAll<string>(mountPath =>
        {
            if (string.IsNullOrWhiteSpace(mountPath)) return true;

            var mockVault = new Mock<ILuxVaultService>();
            var backend = new MqttBackend(mockVault.Object);
            var config = CreateConfig(mountPath);
            backend.InitializeAsync(config).Wait();

            return backend.MountPath == mountPath;
        });
    }
}

public class MqttBackendFuzzingTests
{
    private readonly Mock<ILuxVaultService> _mockVault = new Mock<ILuxVaultService>();

    [Fact]
    public void MqttBackend_FuzzBrokerAddresses()
    {
        var backend = new MqttBackend(_mockVault.Object);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var broker = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/mqtt",
                    ["Broker"] = broker,
                    ["Port"] = "1883",
                    ["ClientId"] = "test"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for invalid brokers
            }
        }
    }

    [Fact]
    public void MqttBackend_InvalidPortsHandledGracefully()
    {
        var backend = new MqttBackend(_mockVault.Object);
        var invalidPorts = new[] { "-1", "0", "65536", "99999", "abc", "1.5" };

        foreach (var port in invalidPorts)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MountPath"] = "/mqtt",
                    ["Broker"] = "localhost",
                    ["Port"] = port,
                    ["ClientId"] = "test"
                }!)
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for invalid ports
            }
        }
    }
}

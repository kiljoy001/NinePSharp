using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends.MQTT;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Tests.Backends;

public class MqttBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task MqttBackend_Initialization_Works()
    {
        var backend = new MqttBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("BrokerUrl", "localhost"),
            new KeyValuePair<string, string?>("ClientId", "test-client"),
            new KeyValuePair<string, string?>("MountPath", "/mqtt")
        }).Build();

        await backend.InitializeAsync(config);
        backend.Name.Should().Be("MQTT");
        backend.MountPath.Should().Be("/mqtt");
    }

    [Fact]
    public async Task MqttFileSystem_Write_PublishesMessage()
    {
        // Arrange
        var transportMock = new Mock<IMqttTransport>();
        transportMock.Setup(t => t.PublishAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var config = new MqttBackendConfig { BrokerUrl = "localhost", ClientId = "test" };
        var fs = new MqttFileSystem(config, transportMock.Object, _vault);

        // Act: Walk to 'sensors/temp' and write data
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "sensors", "temp" }));
        var payload = Encoding.UTF8.GetBytes("22.5");
        await fs.WriteAsync(new Twrite((ushort)1, 2u, 0uL, payload.AsMemory()));

        // Assert
        transportMock.Verify(t => t.PublishAsync("sensors/temp", payload), Times.Once);
    }
}

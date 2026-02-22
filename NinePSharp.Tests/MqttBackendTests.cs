using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.MQTT;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class MqttBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IMqttTransport> _transportMock = new();
    private readonly MqttBackendConfig _config = new() { BrokerUrl = "localhost", ClientId = "test-client" };

    [Fact]
    public async Task Mqtt_Publish_Works()
    {
        // Arrange
        _transportMock.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
                      .Returns(Task.CompletedTask);

        var fs = new MqttFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Walk to /topics/home/sensor
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "topics", "home", "sensor" }));

        // Act - Write Payload
        var payload = Encoding.UTF8.GetBytes("22.5C");
        await fs.WriteAsync(new Twrite(1, 1, 0, payload));

        // Assert
        _transportMock.Verify(x => x.PublishAsync("home/sensor", It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task Mqtt_Subscribe_And_Read_Works()
    {
        // Arrange
        var topic = "home/sensor";
        var message = Encoding.UTF8.GetBytes("incoming-data");
        
        _transportMock.Setup(x => x.SubscribeAsync(topic)).Returns(Task.CompletedTask);
        _transportMock.Setup(x => x.GetNextMessageAsync(topic)).ReturnsAsync(message);

        var fs = new MqttFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Act - Walk (triggers subscribe)
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "topics", "home", "sensor" }));
        
        // Act - Read
        var read = await fs.ReadAsync(new Tread(1, 1, 0, 8192));
        var result = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Equal("incoming-data", result);
        _transportMock.Verify(x => x.SubscribeAsync(topic), Times.Once);
    }
}

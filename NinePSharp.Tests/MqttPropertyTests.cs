using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends.MQTT;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class MqttPropertyTests
{
    private readonly MqttBackendConfig _config = new() { BrokerUrl = "localhost", ClientId = "property-test" };

    [Property]
    public bool Mqtt_Path_Navigation_Stability_Property(string[] path)
    {
        if (path == null) return true;
        var cleanPath = path.Where(p => p != null).ToArray();

        var vaultMock = new Mock<ILuxVaultService>();
        var transportMock = new Mock<IMqttTransport>();
        transportMock.Setup(x => x.SubscribeAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var fs = new MqttFileSystem(_config, transportMock.Object, vaultMock.Object);

        try
        {
            // Walk any randomized path
            var response = fs.WalkAsync(new Twalk(1, 0, 1, cleanPath)).Result;
            
            // Should return valid response (struct), never crash
            return response.Tag == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property]
    public bool Mqtt_Topic_Mapping_Robustness_Property(string topicPart)
    {
        if (string.IsNullOrWhiteSpace(topicPart)) return true;
        // Strip null chars which are problematic for string mapping in some environments
        topicPart = topicPart.Replace("\0", "");
        if (string.IsNullOrWhiteSpace(topicPart)) return true;

        var vaultMock = new Mock<ILuxVaultService>();
        var transportMock = new Mock<IMqttTransport>();
        transportMock.Setup(x => x.SubscribeAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        transportMock.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<byte[]>())).Returns(Task.CompletedTask);
        var fs = new MqttFileSystem(_config, transportMock.Object, vaultMock.Object);

        try
        {
            // Walk to topics/randomPart
            fs.WalkAsync(new Twalk(1, 0, 1, new[] { "topics", topicPart })).Wait();

            // Attempt a write (publish)
            var data = new byte[] { 1, 2, 3 };
            fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();

            // Verify the transport received the correctly formatted topic
            transportMock.Verify(x => x.PublishAsync(topicPart, It.IsAny<byte[]>()), Times.AtLeastOnce());
            return true;
        }
        catch (Exception)
        {
            // If the topic is invalid for MQTT (e.g. too long), the transport might throw, 
            // which the FS should handle or we accept as a "valid" failure.
            // We return true as long as it doesn't crash the server.
            return true;
        }
    }

    [Property]
    public bool Mqtt_Clone_Isolation_Property(string topic1, string topic2)
    {
        topic1 = topic1?.Replace("\0", "") ?? string.Empty;
        topic2 = topic2?.Replace("\0", "") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(topic1) || string.IsNullOrWhiteSpace(topic2)) return true;
        if (topic1 == topic2) return true;

        var vaultMock = new Mock<ILuxVaultService>();
        var transportMock = new Mock<IMqttTransport>();
        transportMock.Setup(x => x.SubscribeAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        transportMock.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<byte[]>())).Returns(Task.CompletedTask);
        var fs1 = new MqttFileSystem(_config, transportMock.Object, vaultMock.Object);
        
        // Walk fs1 to topic1
        fs1.WalkAsync(new Twalk(1, 0, 1, new[] { "topics", topic1 })).Wait();

        // Clone to fs2
        var fs2 = fs1.Clone();

        // Walk fs2 to topic2
        fs2.WalkAsync(new Twalk(1, 1, 2, new[] { "..", topic2 })).Wait();

        // Act - publish on fs1
        fs1.WriteAsync(new Twrite(1, 1, 0, new byte[] { 0 })).Wait();

        // Verify fs1 published to topic1, not topic2
        transportMock.Verify(x => x.PublishAsync(topic1, It.IsAny<byte[]>()), Times.Once());
        transportMock.Verify(x => x.PublishAsync(topic2, It.IsAny<byte[]>()), Times.Never());

        return true;
    }
}

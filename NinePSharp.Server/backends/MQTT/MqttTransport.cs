using System;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;

namespace NinePSharp.Server.Backends.MQTT;

public class MqttTransport : IMqttTransport
{
    // Reverting to stub until namespace issue is resolved or correct one found
    public Task ConnectAsync(string brokerUrl, string clientId, string? user, string? password) => Task.CompletedTask;
    public Task PublishAsync(string topic, byte[] payload) => Task.CompletedTask;
    public Task SubscribeAsync(string topic) => Task.CompletedTask;
}

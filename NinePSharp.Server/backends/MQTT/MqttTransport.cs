using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.MQTT;

public class MqttTransport : IMqttTransport
{
    private IMqttClient? _mqttClient;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> _topicBuffers = new();
    private readonly MqttClientFactory _factory = new();

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    public async Task ConnectAsync(string brokerUrl, string clientId, SecureString? user, SecureString? pass)
    {
        _mqttClient = _factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerUrl)
            .WithClientId(clientId);

        if (user != null && pass != null)
        {
            string u = SecureStringHelper.ToString(user);
            string p = SecureStringHelper.ToString(pass);
            optionsBuilder.WithCredentials(u, p);
        }

        _mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.Payload.ToArray();
            
            var queue = _topicBuffers.GetOrAdd(topic, _ => new ConcurrentQueue<byte[]>());
            queue.Enqueue(payload);
            
            return Task.CompletedTask;
        };

        await _mqttClient.ConnectAsync(optionsBuilder.Build());
    }

    public async Task PublishAsync(string topic, byte[] payload)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected) throw new InvalidOperationException("MQTT not connected.");

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    public async Task SubscribeAsync(string topic)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected) throw new InvalidOperationException("MQTT not connected.");
        
        await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topic))
            .Build());
            
        _topicBuffers.GetOrAdd(topic, _ => new ConcurrentQueue<byte[]>());
    }

    public Task<byte[]?> GetNextMessageAsync(string topic)
    {
        if (_topicBuffers.TryGetValue(topic, out var queue) && queue.TryDequeue(out var payload))
        {
            return Task.FromResult<byte[]?>(payload);
        }
        return Task.FromResult<byte[]?>(null);
    }

    public void Dispose()
    {
        _mqttClient?.Dispose();
    }
}

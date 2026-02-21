using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.MQTT;

public interface IMqttTransport
{
    Task ConnectAsync(string brokerUrl, string clientId, string? user, string? password);
    Task PublishAsync(string topic, byte[] payload);
    Task SubscribeAsync(string topic);
}

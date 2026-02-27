using System.Collections.Generic;
using System.Security;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.MQTT;

public interface IMqttTransport : IDisposable
{
    Task ConnectAsync(string brokerUrl, string clientId, SecureString? user, SecureString? pass);
    Task PublishAsync(string topic, byte[] payload);
    Task SubscribeAsync(string topic);
    
    /// <summary>
    /// Retrieves the next available message for a topic.
    /// Returns null if no message is available.
    /// </summary>
    Task<byte[]?> GetNextMessageAsync(string topic);
    
    bool IsConnected { get; }
}

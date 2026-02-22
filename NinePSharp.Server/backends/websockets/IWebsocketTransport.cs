using System;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.Websockets;

public interface IWebsocketTransport : IDisposable
{
    Task ConnectAsync(string url);
    Task SendAsync(byte[] payload);
    
    /// <summary>
    /// Retrieves the next available message from the internal buffer.
    /// Returns null if no message is pending.
    /// </summary>
    Task<byte[]?> GetNextMessageAsync();
    
    bool IsConnected { get; }
}

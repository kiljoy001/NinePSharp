using System;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.Websockets;

/// <summary>
/// Interface for WebSocket service communication.
/// </summary>
public interface IWebsocketTransport : IDisposable
{
    /// <summary>Connects to the WebSocket server.</summary>
    Task ConnectAsync(string url);
    /// <summary>Sends a message to the WebSocket server.</summary>
    Task SendAsync(byte[] payload);
    
    /// <summary>
    /// Retrieves the next available message from the internal buffer.
    /// Returns null if no message is pending.
    /// </summary>
    Task<byte[]?> GetNextMessageAsync();
    
    /// <summary>Gets a value indicating whether the client is connected.</summary>
    bool IsConnected { get; }
}

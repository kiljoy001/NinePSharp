using System;
using System.Threading.Tasks;
using Websocket.Client;
using System.Reactive.Linq;

namespace NinePSharp.Server.Backends.Websockets;

public class WebsocketTransport : IWebsocketTransport, IDisposable
{
    private WebsocketClient? _client;
    private byte[]? _lastMessage;

    public async Task ConnectAsync(string url)
    {
        _client = new WebsocketClient(new Uri(url));
        
        _client.MessageReceived.Subscribe(msg =>
        {
            _lastMessage = msg.Binary;
        });

        await _client.Start();
    }

    public Task SendAsync(byte[] payload)
    {
        if (_client == null || !_client.IsRunning) throw new InvalidOperationException("WS not connected.");
        _client.Send(payload);
        return Task.CompletedTask;
    }

    public Task<byte[]> ReceiveAsync()
    {
        var msg = _lastMessage;
        _lastMessage = null; // Clear on read
        return Task.FromResult(msg ?? Array.Empty<byte>());
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

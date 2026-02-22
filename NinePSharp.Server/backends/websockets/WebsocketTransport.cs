using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.Websockets;

public class WebsocketTransport : IWebsocketTransport
{
    private ClientWebSocket? _webSocket;
    private readonly ConcurrentQueue<byte[]> _messageBuffer = new();
    private CancellationTokenSource? _cts;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string url)
    {
        _webSocket = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        
        await _webSocket.ConnectAsync(new Uri(url), _cts.Token);
        
        // Start background receive loop
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[8192];
        try {
            while (!token.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                }
                else
                {
                    var data = new byte[result.Count];
                    Array.Copy(buffer, data, result.Count);
                    _messageBuffer.Enqueue(data);
                }
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"[WS Transport] Receive error: {ex.Message}");
        }
    }

    public async Task SendAsync(byte[] payload)
    {
        if (_webSocket?.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket not connected.");
        
        await _webSocket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    public Task<byte[]?> GetNextMessageAsync()
    {
        if (_messageBuffer.TryDequeue(out var message))
        {
            return Task.FromResult<byte[]?>(message);
        }
        return Task.FromResult<byte[]?>(null);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}

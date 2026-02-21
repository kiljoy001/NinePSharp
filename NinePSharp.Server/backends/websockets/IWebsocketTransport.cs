using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.Websockets;

public interface IWebsocketTransport
{
    Task ConnectAsync(string url);
    Task SendAsync(byte[] payload);
    Task<byte[]> ReceiveAsync();
}

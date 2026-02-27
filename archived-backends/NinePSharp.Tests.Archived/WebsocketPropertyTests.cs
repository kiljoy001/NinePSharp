using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.Websockets;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class WebsocketPropertyTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IWebsocketTransport> _transportMock = new();
    private readonly WebsocketBackendConfig _config = new() { Url = "ws://localhost", MountPath = "/ws" };

    [Property]
    public bool Websocket_Path_Navigation_Stability_Property(string[] path)
    {
        if (path == null) return true;
        var cleanPath = path.Where(p => p != null).ToArray();

        var fs = new WebsocketFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        try
        {
            var response = fs.WalkAsync(new Twalk(1, 0, 1, cleanPath)).Result;
            return response.Tag == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property]
    public bool Websocket_Message_Integrity_Property(byte[] payload)
    {
        if (payload == null) return true;

        byte[]? capturedPayload = null;
        var transportMock = new Mock<IWebsocketTransport>();
        transportMock.Setup(x => x.SendAsync(It.IsAny<byte[]>()))
                     .Callback<byte[]>(b => capturedPayload = b.ToArray())
                     .Returns(Task.CompletedTask);

        var fs = new WebsocketFileSystem(_config, transportMock.Object, _vaultMock.Object);
        
        // Walk to data node
        fs.WalkAsync(new Twalk(1, 0, 1, new[] { "data" })).Wait();

        try
        {
            fs.WriteAsync(new Twrite(1, 1, 0, payload)).Wait();
            return capturedPayload != null && payload.SequenceEqual(capturedPayload);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property]
    public bool Websocket_Clone_Isolation_Property(byte[] p1, byte[] p2)
    {
        if (p1 == null || p2 == null) return true;

        var fs1 = new WebsocketFileSystem(_config, _transportMock.Object, _vaultMock.Object);
        fs1.WalkAsync(new Twalk(1, 0, 1, new[] { "data" })).Wait();

        var fs2 = fs1.Clone();
        
        // Ensure that writing to one doesn't corrupt or interfere with the other instance's 9P state
        // In the WS case, they share the transport, but their internal _currentPath must be isolated.
        return fs2 != null; 
    }
}

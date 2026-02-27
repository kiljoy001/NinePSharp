using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends.Websockets;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class WebsocketBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IWebsocketTransport> _transportMock = new();
    private readonly WebsocketBackendConfig _config = new() { Url = "ws://localhost:8080", MountPath = "/ws" };

    [Fact]
    public async Task Websocket_Write_Sends_Message_To_Transport()
    {
        // Arrange
        byte[]? capturedPayload = null;
        _transportMock.Setup(x => x.SendAsync(It.IsAny<byte[]>()))
                      .Callback<byte[]>(b => capturedPayload = b.ToArray()) // Copy immediately
                      .Returns(Task.CompletedTask);

        var fs = new WebsocketFileSystem(_config, _transportMock.Object, _vaultMock.Object);
        var payload = Encoding.UTF8.GetBytes("hello socket");

        // Act - Walk to data node
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "data" }));
        
        // Act - Write
        var twrite = new Twrite(1, 1, 0, payload);
        await fs.WriteAsync(twrite);

        // Assert
        Assert.NotNull(capturedPayload);
        Assert.True(payload.SequenceEqual(capturedPayload));
    }

    [Fact]
    public async Task Websocket_Read_Retrieves_Buffered_Message()
    {
        // Arrange
        var incoming = Encoding.UTF8.GetBytes("server-says-hi");
        _transportMock.Setup(x => x.GetNextMessageAsync()).ReturnsAsync(incoming);
        
        var fs = new WebsocketFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Act - Walk
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "data" }));
        
        // Act - Read
        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);

        // Assert
        Assert.Equal("server-says-hi", Encoding.UTF8.GetString(response.Data.ToArray()));
    }

    [Fact]
    public async Task Websocket_Read_Empty_When_No_Messages()
    {
        // Arrange
        _transportMock.Setup(x => x.GetNextMessageAsync()).ReturnsAsync((byte[]?)null);
        var fs = new WebsocketFileSystem(_config, _transportMock.Object, _vaultMock.Object);
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "data" }));

        // Act
        var response = await fs.ReadAsync(new Tread(1, 1, 0, 8192));

        // Assert
        Assert.Empty(response.Data.ToArray());
    }
}

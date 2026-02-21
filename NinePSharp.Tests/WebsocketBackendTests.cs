using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends.Websockets;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Tests.Backends;

public class WebsocketBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task WebsocketBackend_Initialization_Works()
    {
        var backend = new WebsocketBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Url", "ws://localhost:8080"),
            new KeyValuePair<string, string?>("MountPath", "/ws")
        }).Build();

        await backend.InitializeAsync(config);
        backend.Name.Should().Be("Websockets");
        backend.MountPath.Should().Be("/ws");
    }

    [Fact]
    public async Task WebsocketFileSystem_Write_SendsFrame()
    {
        // Arrange
        var transportMock = new Mock<IWebsocketTransport>();
        transportMock.Setup(t => t.SendAsync(It.IsAny<byte[]>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var config = new WebsocketBackendConfig { Url = "ws://localhost:8080" };
        var fs = new WebsocketFileSystem(config, transportMock.Object, _vault);

        // Act: Write data to root or 'data' node
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "data" }));
        var payload = Encoding.UTF8.GetBytes("hello ws");
        await fs.WriteAsync(new Twrite((ushort)1, 2u, 0uL, payload.AsMemory()));

        // Assert
        transportMock.Verify(t => t.SendAsync(payload), Times.Once);
    }
}

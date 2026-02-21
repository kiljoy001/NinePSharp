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
using NinePSharp.Server.Backends.gRPC;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Tests.Backends;

public class GrpcBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task GrpcBackend_Initialization_Works()
    {
        var backend = new GrpcBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Host", "localhost"),
            new KeyValuePair<string, string?>("Port", "50051"),
            new KeyValuePair<string, string?>("MountPath", "/grpc")
        }).Build();

        await backend.InitializeAsync(config);
        backend.Name.Should().Be("gRPC");
        backend.MountPath.Should().Be("/grpc");
    }

    [Fact]
    public async Task GrpcFileSystem_Write_CallsServiceMethod()
    {
        // Arrange
        var transportMock = new Mock<IGrpcTransport>();
        transportMock.Setup(t => t.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("{\"result\": \"ok\"}"))
            .Verifiable();

        var config = new GrpcBackendConfig { Host = "localhost", Port = 50051 };
        var fs = new GrpcFileSystem(config, transportMock.Object, _vault);

        // Act: Walk to 'Greeter/SayHello' and write data
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "Greeter", "SayHello" }));
        var payload = Encoding.UTF8.GetBytes("{\"name\": \"world\"}");
        await fs.WriteAsync(new Twrite((ushort)1, 2u, 0uL, payload.AsMemory()));

        // Assert
        transportMock.Verify(t => t.CallAsync("Greeter", "SayHello", payload), Times.Once);
    }
}

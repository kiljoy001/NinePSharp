using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.gRPC;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class GrpcBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IGrpcTransport> _transportMock = new();
    private readonly GrpcBackendConfig _config = new() { Host = "localhost", Port = 50051 };

    [Fact]
    public async Task Grpc_Method_Call_Works_And_Stores_Response()
    {
        // Arrange
        var responsePayload = new byte[] { 0x08, 0x01 }; // Protobuf: field 1, value 1
        _transportMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<IDictionary<string, string>>()))
                      .ReturnsAsync(responsePayload);

        var fs = new GrpcFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Walk to /services/Greeter/SayHello
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "services", "Greeter", "SayHello" }));

        // Act - Write Request
        var requestPayload = new byte[] { 0x0A, 0x05, 0x57, 0x6F, 0x72, 0x6C, 0x64 }; // Protobuf: field 1, length 5, "World"
        await fs.WriteAsync(new Twrite(1, 1, 0, requestPayload));

        // Act - Read Response
        var read = await fs.ReadAsync(new Tread(1, 1, 0, 8192));
        var result = read.Data.ToArray();

        // Assert
        Assert.True(responsePayload.SequenceEqual(result));
        _transportMock.Verify(x => x.CallAsync("Greeter", "SayHello", It.IsAny<byte[]>(), It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task Grpc_Metadata_Is_Passed_To_Transport()
    {
        // Arrange
        IDictionary<string, string>? capturedMetadata = null;
        _transportMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<IDictionary<string, string>>()))
                      .Callback<string, string, byte[], IDictionary<string, string>>((s, m, p, meta) => capturedMetadata = meta)
                      .ReturnsAsync(Array.Empty<byte>());

        var fs = new GrpcFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Walk to .metadata and write
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { ".metadata" }));
        await fs.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes("authorization: Bearer token123\ncustom-header: value")));

        // Walk to method
        var methodFs = fs.Clone();
        await methodFs.WalkAsync(new Twalk(1, 0, 2, new[] { "..", "services", "Greeter", "SayHello" }));

        // Act
        await methodFs.WriteAsync(new Twrite(1, 2, 0, Array.Empty<byte>()));

        // Assert
        Assert.NotNull(capturedMetadata);
        Assert.Equal("Bearer token123", capturedMetadata["authorization"]);
        Assert.Equal("value", capturedMetadata["custom-header"]);
    }
}

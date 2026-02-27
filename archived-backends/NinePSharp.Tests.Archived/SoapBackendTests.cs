using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.SOAP;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class SoapBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<ISoapTransport> _transportMock = new();
    private readonly SoapBackendConfig _config = new() { MountPath = "/soap", WsdlUrl = "http://api.test?wsdl" };

    [Fact]
    public async Task Soap_Action_Call_Works_And_Stores_Response()
    {
        // Arrange
        var responseXml = "<Response>Success</Response>";
        _transportMock.Setup(x => x.CallActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                      .ReturnsAsync(responseXml);

        var fs = new SoapFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Walk to /actions/GetInfo
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "actions", "GetInfo" }));

        // Act - Write Request
        var requestXml = "<Request>Data</Request>";
        await fs.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes(requestXml)));

        // Act - Read Response
        var read = await fs.ReadAsync(new Tread(1, 1, 0, 8192));
        var result = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Equal(responseXml, result);
        _transportMock.Verify(x => x.CallActionAsync("GetInfo", requestXml, It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task Soap_Headers_Are_Passed_To_Transport()
    {
        // Arrange
        IDictionary<string, string>? capturedHeaders = null;
        _transportMock.Setup(x => x.CallActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                      .Callback<string, string, IDictionary<string, string>>((a, p, h) => capturedHeaders = h)
                      .ReturnsAsync("<OK/>");

        var rootFs = new SoapFileSystem(_config, _transportMock.Object, _vaultMock.Object);

        // Walk to .headers and write
        var headerFs = rootFs.Clone();
        await headerFs.WalkAsync(new Twalk(1, 0, 1, new[] { ".headers" }));
        await headerFs.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes("SoapAction: Test\nAuth: Key123")));

        // Walk from root to action using the instance that has the headers
        // Since Clone() copies headers, we can walk from headerFs back to root then to action,
        // or just ensure headerFs is at root and walk.
        // Actually, let's just use a fresh clone for the action call, ensuring headers are preserved.
        var actionFs = headerFs.Clone();
        // Walk back to root (..), then to actions/DoWork
        await actionFs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "actions", "DoWork" }));

        // Act
        await actionFs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("<Work/>")));

        // Assert
        Assert.NotNull(capturedHeaders);
        Assert.Equal("Test", capturedHeaders["SoapAction"]);
        Assert.Equal("Key123", capturedHeaders["Auth"]);
    }
}

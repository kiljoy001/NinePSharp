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
using NinePSharp.Server.Backends.SOAP;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Tests.Backends;

public class SoapBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task SoapBackend_Initialization_Works()
    {
        var backend = new SoapBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("WsdlUrl", "http://example.com/service?wsdl"),
            new KeyValuePair<string, string?>("MountPath", "/soap")
        }).Build();

        await backend.InitializeAsync(config);
        backend.Name.Should().Be("SOAP");
        backend.MountPath.Should().Be("/soap");
    }

    [Fact]
    public async Task SoapFileSystem_Write_CallsSoapAction()
    {
        // Arrange
        var transportMock = new Mock<ISoapTransport>();
        transportMock.Setup(t => t.CallActionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("<response>ok</response>")
            .Verifiable();

        var config = new SoapBackendConfig { WsdlUrl = "http://example.com/service?wsdl" };
        var fs = new SoapFileSystem(config, transportMock.Object, _vault);

        // Act: Walk to 'GetWeather' and write XML data
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "GetWeather" }));
        var payload = Encoding.UTF8.GetBytes("<city>London</city>");
        await fs.WriteAsync(new Twrite((ushort)1, 2u, 0uL, payload.AsMemory()));

        // Assert
        transportMock.Verify(t => t.CallActionAsync("GetWeather", "<city>London</city>"), Times.Once);
    }
}

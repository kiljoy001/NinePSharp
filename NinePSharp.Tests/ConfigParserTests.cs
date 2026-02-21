using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Configuration.Parser;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using System.Collections.Generic;

namespace NinePSharp.Tests.Configuration;

public class ConfigParserTests
{
    [Fact]
    public void Bind_Should_Populate_EndpointConfig_Correcty()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"Server:Endpoints:0:Address", "127.0.0.1"},
            {"Server:Endpoints:0:Port", "5640"},
            {"Server:Endpoints:0:Protocol", "tcp"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var parser = new ConfigParser();

        // Act
        var config = parser.Bind<ServerConfig>(configuration, "Server");

        // Assert
        Assert.Single(config.Endpoints);
        Assert.Equal("127.0.0.1", config.Endpoints[0].Address);
        Assert.Equal(5640, config.Endpoints[0].Port);
        Assert.Equal("tcp", config.Endpoints[0].Protocol);
    }

    [Fact]
    public void Bind_Should_Handle_Missing_Section()
    {
        // Arrange
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var parser = new ConfigParser();

        // Act
        var config = parser.Bind<ServerConfig>(configuration, "NonExistent");

        // Assert
        Assert.NotNull(config);
        Assert.Empty(config.Endpoints);
    }

    [Property]
    public bool Bind_Should_Roundtrip_Arbitrary_ServerConfig(string address, int port, string protocol, string restBaseUrl)
    {
        if (address == null || protocol == null || restBaseUrl == null) return true;

        // Arrange
        var inMemorySettings = new Dictionary<string, string?> {
            {"Server:Endpoints:0:Address", address},
            {"Server:Endpoints:0:Port", port.ToString()},
            {"Server:Endpoints:0:Protocol", protocol},
            {"Server:Rest:BaseUrl", restBaseUrl}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var parser = new ConfigParser();

        // Act
        var config = parser.Bind<ServerConfig>(configuration, "Server");

        // Assert
        return config.Endpoints.Count == 1 &&
               config.Endpoints[0].Address == address &&
               config.Endpoints[0].Port == port &&
               config.Endpoints[0].Protocol == protocol &&
               config.Rest != null &&
               config.Rest.BaseUrl == restBaseUrl;
    }
}

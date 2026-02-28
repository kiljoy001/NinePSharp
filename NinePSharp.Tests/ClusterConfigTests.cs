using NinePSharp.Constants;
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Moq;
using NinePSharp.Server.Configuration.Models;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterConfigTests
{
    [Fact]
    public void LoadFromTextFile_Parses_Bind_And_Interface_Settings()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, new[]
            {
                "SystemName = NinePCluster",
                "Hostname = 127.0.0.1",
                "Port = 8081",
                "Role = backend",
                "BindHostname = 0.0.0.0",
                "BindPort = 18081",
                "PublicHostname = 200:abcd::1234",
                "PublicPort = 28081",
                "InterfaceName = ygg0",
                "PreferIPv6 = true",
                "Seed = akka.tcp://NinePCluster@127.0.0.1:8081"
            });

            var logger = new Mock<ILogger>();
            var config = ClusterConfigLoader.LoadFromTextFile(path, logger.Object);

            Assert.Equal("NinePCluster", config.SystemName);
            Assert.Equal("127.0.0.1", config.Hostname);
            Assert.Equal(8081, config.Port);
            Assert.Equal("backend", config.Role);
            Assert.Equal("0.0.0.0", config.BindHostname);
            Assert.Equal(18081, config.BindPort);
            Assert.Equal("200:abcd::1234", config.PublicHostname);
            Assert.Equal(28081, config.PublicPort);
            Assert.Equal("ygg0", config.InterfaceName);
            Assert.True(config.PreferIPv6);
            Assert.Single(config.SeedNodes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildHocon_Uses_Bind_And_Public_Overrides()
    {
        var config = new AkkaConfig
        {
            SystemName = "NinePCluster",
            Hostname = "10.1.0.20",
            Port = 8081,
            BindHostname = "0.0.0.0",
            BindPort = 18081,
            PublicHostname = "200:abcd::1234",
            PublicPort = 28081,
            Role = "backend",
            SeedNodes = { "akka.tcp://NinePCluster@200:abcd::1234:8081" }
        };

        var hocon = ClusterManager.BuildHocon(config);

        Assert.Contains("hostname = \"200:abcd::1234\"", hocon, StringComparison.Ordinal);
        Assert.Contains("port = 28081", hocon, StringComparison.Ordinal);
        Assert.Contains("bind-hostname = \"0.0.0.0\"", hocon, StringComparison.Ordinal);
        Assert.Contains("bind-port = 18081", hocon, StringComparison.Ordinal);
        Assert.Contains("seed-nodes = [\"akka.tcp://NinePCluster@200:abcd::1234:8081\"]", hocon, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveHostFromInterface_ReturnsNull_WhenInterfaceMissing()
    {
        var config = new AkkaConfig
        {
            InterfaceName = "definitely-does-not-exist-12345",
            PreferIPv6 = true
        };

        var resolved = ClusterManager.ResolveHostFromInterface(config);

        Assert.Null(resolved);
    }
}

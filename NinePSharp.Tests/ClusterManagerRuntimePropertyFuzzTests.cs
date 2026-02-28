using NinePSharp.Constants;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Server.Cluster;
using Xunit;

namespace NinePSharp.Tests;

[Collection("Cluster Manager Runtime")]
public class ClusterManagerRuntimePropertyFuzzTests
{
    private static readonly object ClusterConfGate = new();

    [Fact]
    public async Task ClusterManager_Start_Without_Akka_Config_Stays_Standalone()
    {
        var sut = CreateManager(null);

        sut.Start();

        sut.System.Should().BeNull();
        sut.Registry.Should().BeNull();

        Func<Task> stop = sut.StopAsync;
        await stop.Should().NotThrowAsync();
        sut.Dispose();
    }

    [Fact]
    public async Task ClusterManager_Start_Uses_cluster_conf_Overrides_When_File_Present()
    {
        var systemName = "FileOverride" + Guid.NewGuid().ToString("N")[..8];
        var akkaConfig = new AkkaConfig
        {
            SystemName = "JsonConfigSystem",
            Hostname = "localhost",
            Port = 8081,
            Role = "backend"
        };

        var sut = CreateManager(akkaConfig);

        lock (ClusterConfGate)
        {
            WithClusterConf(new[]
            {
                $"SystemName = {systemName}",
                "Hostname = 127.0.0.1",
                "Port = 0",
                "Role = backend",
            }, () =>
            {
                sut.Start();

                sut.System.Should().NotBeNull();
                sut.Registry.Should().NotBeNull();
                sut.System!.Name.Should().Be(systemName);
            });
        }

        await sut.StopAsync();
        sut.Dispose();
    }

    [Fact]
    public async Task ClusterManager_Start_With_Akka_Config_Creates_And_Terminates_ActorSystem()
    {
        var systemName = "AkkaRuntime" + Guid.NewGuid().ToString("N")[..8];
        var sut = CreateManager(new AkkaConfig
        {
            SystemName = systemName,
            Hostname = "127.0.0.1",
            Port = 0,
            Role = "backend"
        });

        sut.Start();

        sut.System.Should().NotBeNull();
        sut.Registry.Should().NotBeNull();
        sut.System!.Name.Should().Be(systemName);

        var system = sut.System;
        await sut.StopAsync();
        system.WhenTerminated.IsCompleted.Should().BeTrue();

        sut.Dispose();
    }

    [Property(MaxTest = 40)]
    public void BuildHocon_Fallback_And_Escaping_Invariants(
        PositiveInt portSeed,
        bool usePublicHost,
        bool useBindHost,
        bool usePublicPort,
        bool useBindPort)
    {
        int basePort = Math.Clamp(portSeed.Get % 45000 + 1000, 1000, 46000);
        var config = new AkkaConfig
        {
            SystemName = "Sys_" + (portSeed.Get % 9999),
            Hostname = "host\\name\"x",
            Port = basePort,
            Role = "role\\\"y"
        };

        if (usePublicHost) config.PublicHostname = "pub\\name\"z";
        if (useBindHost) config.BindHostname = "bind\\name\"q";
        if (usePublicPort) config.PublicPort = basePort + 1;
        if (useBindPort) config.BindPort = basePort + 2;

        config.SeedNodes.Add("akka.tcp://Seed@127.0.0.1:12345");

        var hocon = ClusterManager.BuildHocon(config);

        var expectedHost = usePublicHost ? config.PublicHostname! : config.Hostname;
        var expectedBindHost = useBindHost ? config.BindHostname! : config.Hostname;
        var expectedPublicPort = usePublicPort ? config.PublicPort!.Value : config.Port;
        var expectedBindPort = useBindPort ? config.BindPort!.Value : config.Port;

        hocon.Should().Contain($"hostname = \"{EscapeForHocon(expectedHost)}\"");
        hocon.Should().Contain($"bind-hostname = \"{EscapeForHocon(expectedBindHost)}\"");
        hocon.Should().Contain($"port = {expectedPublicPort}");
        hocon.Should().Contain($"bind-port = {expectedBindPort}");
        hocon.Should().Contain($"roles = [\"{EscapeForHocon(config.Role)}\"]");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ResolveHostFromInterface_Blank_InterfaceName_Returns_Null(string interfaceName)
    {
        var config = new AkkaConfig { InterfaceName = interfaceName };

        var resolved = ClusterManager.ResolveHostFromInterface(config);

        resolved.Should().BeNull();
    }

    private static ClusterManager CreateManager(AkkaConfig? config)
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);

        return new ClusterManager(
            NullLogger<ClusterManager>.Instance,
            loggerFactory.Object,
            config);
    }

    private static string EscapeForHocon(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void WithClusterConf(string[] lines, Action action)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "cluster.conf");
        string? backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            File.WriteAllLines(path, lines);
            action();
        }
        finally
        {
            if (backup == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                File.WriteAllText(path, backup);
            }
        }
    }
}

[CollectionDefinition("Cluster Manager Runtime", DisableParallelization = true)]
public sealed class ClusterManagerRuntimeCollection
{
}

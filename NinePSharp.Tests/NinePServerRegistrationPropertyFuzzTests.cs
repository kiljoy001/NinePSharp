using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public sealed class NinePServerRegistrationPropertyFuzzTests
{
    [Property(MaxTest = 40)]
    public bool AddNinePSharpServer_Registers_No_Backends_By_Default(bool withEmercoinSection)
    {
        var services = new ServiceCollection();
        IConfiguration config = BuildConfiguration(withEmercoinSection);

        services.AddNinePSharpServer(config);

        int backendRegistrations = services.Count(d => d.ServiceType == typeof(IProtocolBackend));
        bool hasNullRemoteProvider = services.Any(d => d.ServiceType == typeof(IRemoteMountProvider) && d.ImplementationType == typeof(NullRemoteMountProvider));
        bool hasHostedNinePServer = services.Any(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));

        return backendRegistrations == 0 && hasNullRemoteProvider && hasHostedNinePServer;
    }

    [Fact]
    public void AddNinePSharpBackend_Generic_Register_Is_Enumerable_And_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddNinePSharpServer(BuildConfiguration(false));
        services.AddNinePSharpBackend<StubBackend>();
        services.AddNinePSharpBackend<StubBackend>();

        using var provider = services.BuildServiceProvider();
        var backends = provider.GetServices<IProtocolBackend>().ToArray();

        backends.Should().ContainSingle();
        backends[0].Should().BeOfType<StubBackend>();
    }

    [Fact]
    public void AddNinePSharpBackend_Factory_Register_Adds_Backend_Instance()
    {
        var services = new ServiceCollection();
        services.AddNinePSharpServer(BuildConfiguration(false));
        services.AddNinePSharpBackend(_ => new FactoryBackend("/factory"));

        using var provider = services.BuildServiceProvider();
        var backends = provider.GetServices<IProtocolBackend>().ToArray();

        backends.Should().ContainSingle();
        backends[0].MountPath.Should().Be("/factory");
    }

    [Fact]
    public void AddNinePSharpAkkaCluster_Replaces_Default_RemoteMountProvider()
    {
        var services = new ServiceCollection();
        services.AddNinePSharpServer(BuildConfiguration(false));
        services.AddNinePSharpAkkaCluster();

        using var provider = services.BuildServiceProvider();
        var remoteMountProvider = provider.GetRequiredService<IRemoteMountProvider>();

        remoteMountProvider.Should().BeOfType<ClusterManager>();
    }

    [Fact]
    public void AddNinePSharpServer_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddNinePSharpServer(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IConfiguration BuildConfiguration(bool withEmercoinSection)
    {
        var data = new Dictionary<string, string?>
        {
            ["Server:Endpoints:0:Address"] = "127.0.0.1",
            ["Server:Endpoints:0:Port"] = "5641",
            ["Server:Endpoints:0:Protocol"] = "tcp"
        };

        if (withEmercoinSection)
        {
            data["Server:Emercoin:EndpointUrl"] = "http://127.0.0.1:6662/";
            data["Server:Emercoin:Username"] = "u";
            data["Server:Emercoin:Password"] = "p";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private sealed class StubBackend : IProtocolBackend
    {
        public string Name => "stub";
        public string MountPath => "/stub";
        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
        public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => new MockFileSystem("stub");
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new MockFileSystem("stub");
    }

    private sealed class FactoryBackend : IProtocolBackend
    {
        public FactoryBackend(string mountPath)
        {
            MountPath = mountPath;
        }

        public string Name => "factory";
        public string MountPath { get; }
        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
        public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => new MockFileSystem(MountPath);
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new MockFileSystem(MountPath);
    }
}

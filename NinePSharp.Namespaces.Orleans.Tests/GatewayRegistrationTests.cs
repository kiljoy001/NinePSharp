using FsCheck.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using NinePSharp.Server;
using Orleans;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class GatewayRegistrationTests
{
    [Property(MaxTest = 100)]
    public bool ProviderResolvesExactInterfaceAndDevice(Guid device)
    {
        string key = device.ToString("N");
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var grain = new Mock<IMountableResourceGrain>().Object;
        factory.Setup(value => value.GetGrain<IMountableResourceGrain>(key, null)).Returns(grain);
        var resolver = new RegisteredMountableResourceResolver(factory.Object,
            new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("resource") });
        return ReferenceEquals(grain, resolver.Resolve(new ResourceIdentityModel("resource", key, 1)));
    }

    [Fact]
    public void ProviderNamesAreOrdinalAndUnknownProvidersFailClosed()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var registration = ResourceProviderRegistration.For<IMountableResourceGrain>("Files");
        var resolver = new RegisteredMountableResourceResolver(factory.Object, new[] { registration });
        Assert.Equal("Files", registration.Provider);
        Assert.Throws<FileNotFoundException>(() => resolver.Resolve(new("files", "device", 1)));
        Assert.Throws<FileNotFoundException>(() => resolver.Resolve(new("unknown", "device", 1)));
        factory.VerifyNoOtherCalls();
        Assert.Throws<ArgumentException>(() => new RegisteredMountableResourceResolver(factory.Object, new[] { registration, registration }));
        _ = new RegisteredMountableResourceResolver(factory.Object,
            new[] { registration, ResourceProviderRegistration.For<IMountableResourceGrain>("files") });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void EmptyProviderAndDeviceAreRejected(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => ResourceProviderRegistration.For<IMountableResourceGrain>(value!));
        var resolver = new RegisteredMountableResourceResolver(new Mock<IGrainFactory>().Object,
            new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("test") });
        Assert.ThrowsAny<ArgumentException>(() => resolver.Resolve(new("test", value!, 1)));
    }

    [Fact]
    public void NullDependenciesAreRejected()
    {
        var factory = new Mock<IGrainFactory>().Object;
        Assert.Throws<ArgumentNullException>(() => new RegisteredMountableResourceResolver(null!, Array.Empty<ResourceProviderRegistration>()));
        Assert.Throws<ArgumentNullException>(() => new RegisteredMountableResourceResolver(factory, null!));
        var resolver = new RegisteredMountableResourceResolver(factory, Array.Empty<ResourceProviderRegistration>());
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => NinePOrleansServiceCollectionExtensions.AddNinePOrleans<TestAttachResolver>(null!));
        Assert.Throws<ArgumentNullException>(() => NinePOrleansServiceCollectionExtensions.AddNinePResource<IMountableResourceGrain>(null!, "test"));
        Assert.Throws<ArgumentNullException>(() => NinePOrleansServiceCollectionExtensions.AddNinePOrleansListener(null!));
    }

    [Fact]
    public void RegistrationsComposeWithoutDuplicatingSharedServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IGrainFactory>().Object);
        Assert.Same(services, services.AddNinePOrleans<TestAttachResolver>());
        services.AddNinePOrleans<TestAttachResolver>();
        Assert.Same(services, services.AddNinePResource<IMountableResourceGrain>("test"));
        Assert.Same(services, services.AddNinePOrleansListener(options => options.Endpoint.Port = 0));
        services.AddNinePOrleansListener();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<TestAttachResolver>(provider.GetRequiredService<IDistributedNamespaceAttachResolver>());
        Assert.IsType<RegisteredMountableResourceResolver>(provider.GetRequiredService<IMountableResourceResolver>());
        Assert.Same(provider.GetRequiredService<IResourceOperations>(), provider.GetRequiredService<IResourceDataOperations>());
        Assert.IsType<OrleansResourceOperations>(provider.GetRequiredService<IResourceDataOperations>());
        Assert.Same(provider.GetRequiredService<DistributedNamespaceDispatcher>(), provider.GetRequiredService<INinePFSDispatcher>());
        Assert.Same(provider.GetRequiredService<NinePOrleansListener>(), Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Equal(0, provider.GetRequiredService<IOptions<NinePOrleansListenerOptions>>().Value.Endpoint.Port);
    }

    [Fact]
    public void ApplicationOverridesArePreserved()
    {
        var services = new ServiceCollection();
        var attach = new TestAttachResolver();
        var resolver = new Mock<IMountableResourceResolver>().Object;
        services.AddSingleton(new Mock<IGrainFactory>().Object);
        services.AddSingleton<IDistributedNamespaceAttachResolver>(attach);
        services.AddSingleton(resolver);
        services.AddNinePOrleans<TestAttachResolver>();
        using var provider = services.BuildServiceProvider();
        Assert.Same(attach, provider.GetRequiredService<IDistributedNamespaceAttachResolver>());
        Assert.Same(resolver, provider.GetRequiredService<IMountableResourceResolver>());
    }

    [Fact]
    public void ListenerRegistrationProvidesDefaultOptionsWithoutAConfigureCallback()
    {
        var services = new ServiceCollection();
        services.AddNinePOrleansListener();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NinePOrleansListenerOptions>>().Value;
        Assert.Equal(5640, options.Endpoint.Port);
        Assert.Equal(256, options.MaxConnections);
    }

    [Fact]
    public void ListenerDefaultsAreLoopbackAndAdmissionIsBounded()
    {
        var options = new NinePOrleansListenerOptions();
        Assert.Equal("127.0.0.1", options.Endpoint.Address);
        Assert.Equal(5640, options.Endpoint.Port);
        Assert.Equal("tcp", options.Endpoint.Protocol);
        Assert.Equal(256, options.MaxConnections);
        using var listener = CreateListener(options);
    }

    [Theory]
    [InlineData(0, "tcp", null)]
    [InlineData(-1, "tcp", null)]
    [InlineData(1, "udp", null)]
    [InlineData(1, "tls", null)]
    [InlineData(1, "tls", " ")]
    public void InvalidListenerConfigurationFailsBeforeBinding(int maximum, string protocol, string? certificate)
    {
        var options = new NinePOrleansListenerOptions { MaxConnections = maximum };
        options.Endpoint.Protocol = protocol;
        options.Endpoint.ServerCertificatePath = certificate!;
        Assert.ThrowsAny<ArgumentException>(() => CreateListener(options));
    }

    [Fact]
    public void ListenerRequiresOptionsAndEndpoint()
    {
        var dispatcher = new GatewayTestContext().Dispatcher;
        Assert.Throws<ArgumentNullException>(() => new NinePOrleansListener(dispatcher, null!, NullLogger<NinePOrleansListener>.Instance));
        Assert.Throws<ArgumentException>(() => CreateListener(new NinePOrleansListenerOptions { Endpoint = null! }));
        var tls = new NinePOrleansListenerOptions();
        tls.Endpoint.Protocol = "tls";
        tls.Endpoint.ServerCertificatePath = "application-supplied.pfx";
        using var listener = CreateListener(tls);
    }

    [Fact]
    public async Task CancelledStartDoesNotBind()
    {
        using var listener = CreateListener(new NinePOrleansListenerOptions());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.StartAsync(new CancellationToken(true)));
    }

    private static NinePOrleansListener CreateListener(NinePOrleansListenerOptions options)
        => new(new GatewayTestContext().Dispatcher, Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
}

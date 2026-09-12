using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NinePSharp.Server;

namespace NinePSharp.Namespaces.Orleans.Server;

/// <summary>Registers the Orleans namespace data plane and optional TCP/TLS endpoint.</summary>
public static class NinePOrleansServiceCollectionExtensions
{
    /// <summary>
    /// Registers a dispatcher using an application-supplied attach/authentication policy.
    /// The host must also register an Orleans silo or connected client and resource providers.
    /// </summary>
    public static IServiceCollection AddNinePOrleans<TAttachResolver>(this IServiceCollection services)
        where TAttachResolver : class, IDistributedNamespaceAttachResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.TryAddSingleton<IDistributedNamespaceAttachResolver, TAttachResolver>();
        services.TryAddSingleton<IMountableResourceResolver, RegisteredMountableResourceResolver>();
        services.TryAddSingleton<OrleansResourceOperations>();
        services.TryAddSingleton<IResourceOperations>(static provider => provider.GetRequiredService<OrleansResourceOperations>());
        services.TryAddSingleton<IResourceDataOperations>(static provider => provider.GetRequiredService<OrleansResourceOperations>());
        services.TryAddSingleton<DistributedNamespaceOperations>();
        services.TryAddSingleton<DistributedNamespaceDispatcher>();
        services.TryAddSingleton<INinePFSDispatcher>(static provider => provider.GetRequiredService<DistributedNamespaceDispatcher>());
        return services;
    }

    /// <summary>Routes one provider name to a typed resource grain, keyed by device.</summary>
    public static IServiceCollection AddNinePResource<TGrain>(this IServiceCollection services, string provider)
        where TGrain : class, IMountableResourceGrain
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(ResourceProviderRegistration.For<TGrain>(provider));
        return services;
    }

    /// <summary>Adds one managed listener. The default endpoint is loopback TCP port 5640.</summary>
    public static IServiceCollection AddNinePOrleansListener(
        this IServiceCollection services,
        Action<NinePOrleansListenerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<NinePOrleansListenerOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<NinePOrleansListener>();
        services.AddHostedService(static provider => provider.GetRequiredService<NinePOrleansListener>());
        return services;
    }
}

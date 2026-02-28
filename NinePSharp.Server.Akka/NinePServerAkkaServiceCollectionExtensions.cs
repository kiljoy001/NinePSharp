using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

public static class NinePServerAkkaServiceCollectionExtensions
{
    public static IServiceCollection AddNinePSharpAkkaCluster(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new AkkaConfig());
        services.Replace(ServiceDescriptor.Singleton<IRemoteMountProvider, ClusterManager>());
        return services;
    }

    public static IServiceCollection AddNinePSharpAkkaCluster(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var akkaConfig = new AkkaConfig();
        configuration.GetSection("Server:Akka").Bind(akkaConfig);
        services.Replace(ServiceDescriptor.Singleton(akkaConfig));
        return services.AddNinePSharpAkkaCluster();
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Configuration.Parser;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server;

public static class NinePServerServiceCollectionExtensions
{
    public static IServiceCollection AddNinePSharpServer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var serverConfigSection = configuration.GetSection("Server");
        var serverConfig = new ServerConfig();
        serverConfigSection.Bind(serverConfig);

        services.Configure<ServerConfig>(serverConfigSection);
        services.AddSingleton(serverConfig);

        if (serverConfig.Emercoin != null)
        {
            services.AddSingleton(serverConfig.Emercoin);
            services.Configure<EmercoinConfig>(configuration.GetSection("Server:Emercoin"));
        }

        services.AddHttpClient();
        services.AddSingleton<IEmercoinNvsClient, EmercoinNvsClient>();
        services.AddSingleton<IEmercoinAuthService, EmercoinAuthService>();
        services.TryAddSingleton<IRemoteMountProvider, NullRemoteMountProvider>();
        services.AddSingleton<IParser, ConfigParser>();

        services.AddSingleton<INinePFSDispatcher, NinePFSDispatcher>();
        services.AddHostedService<NinePServer>();

        return services;
    }

    public static IServiceCollection AddNinePSharpBackend<TBackend>(this IServiceCollection services)
        where TBackend : class, IProtocolBackend
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolBackend, TBackend>());
        return services;
    }

    public static IServiceCollection AddNinePSharpBackend(this IServiceCollection services, Func<IServiceProvider, IProtocolBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(factory);
        return services;
    }
}

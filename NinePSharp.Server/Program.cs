using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Configuration.Parser;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "encrypt")
        {
            var masterKey = Environment.GetEnvironmentVariable("LUX_MASTER_KEY");
            if (string.IsNullOrEmpty(masterKey))
            {
                Console.WriteLine("Error: LUX_MASTER_KEY environment variable not set.");
                return;
            }
            var secret = args[1];
            var protectedSecret = LuxVault.ProtectConfig(secret, masterKey);
            Console.WriteLine($"Protected secret: {protectedSecret}");
            return;
        }

        var host = Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("config.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((hostContext, services) =>
            {
                var serverConfigSection = hostContext.Configuration.GetSection("Server");
                services.Configure<ServerConfig>(serverConfigSection);
                
                // Manual resolution of secrets in configuration before DI registration
                var serverConfig = new ServerConfig();
                serverConfigSection.Bind(serverConfig);
                var masterKey = hostContext.Configuration["LUX_MASTER_KEY"];
                ConfigSecretResolver.ResolveSecrets(serverConfig, masterKey);
                
                // Re-register the resolved config so typed services can use it
                services.AddSingleton(serverConfig);
                if (serverConfig.Rest != null) services.AddSingleton(serverConfig.Rest);
                if (serverConfig.Grpc != null) services.AddSingleton(serverConfig.Grpc);
                if (serverConfig.Mqtt != null) services.AddSingleton(serverConfig.Mqtt);
                if (serverConfig.Soap != null) services.AddSingleton(serverConfig.Soap);
                if (serverConfig.JsonRpc != null) services.AddSingleton(serverConfig.JsonRpc);
                if (serverConfig.Database != null) services.AddSingleton(serverConfig.Database);
                if (serverConfig.Ethereum != null) services.AddSingleton(serverConfig.Ethereum);
                if (serverConfig.Bitcoin != null) services.AddSingleton(serverConfig.Bitcoin);
                if (serverConfig.Solana != null) services.AddSingleton(serverConfig.Solana);
                if (serverConfig.Stellar != null) services.AddSingleton(serverConfig.Stellar);
                if (serverConfig.Cardano != null) services.AddSingleton(serverConfig.Cardano);
                if (serverConfig.Secret != null) services.AddSingleton(serverConfig.Secret);

                services.AddSingleton<ILuxVaultService, LuxVaultService>();
                services.AddSingleton<IParser, ConfigParser>();
                
                // Register backends if they have config
                if (serverConfig.Database != null) services.AddSingleton<IProtocolBackend, DatabaseBackend>();
                if (serverConfig.Ethereum != null) services.AddSingleton<IProtocolBackend, EthereumBackend>();
                if (serverConfig.Bitcoin != null) services.AddSingleton<IProtocolBackend, BitcoinBackend>();
                if (serverConfig.Solana != null) services.AddSingleton<IProtocolBackend, SolanaBackend>();
                if (serverConfig.Stellar != null) services.AddSingleton<IProtocolBackend, StellarBackend>();
                if (serverConfig.Cardano != null) services.AddSingleton<IProtocolBackend, CardanoBackend>();
                if (serverConfig.Secret != null) services.AddSingleton<IProtocolBackend, SecretBackend>();

                services.AddSingleton<NinePFSDispatcher>();
                services.AddHostedService<NinePServer>();
            })
            .Build();

        await host.RunAsync();
    }
}

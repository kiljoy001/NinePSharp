using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
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
    private static SecureString Generate64BitSecureSeed()
    {
        byte[] seedBytes = new byte[8]; // 64 bits
        RandomNumberGenerator.Fill(seedBytes);
        
        var hex = Convert.ToHexString(seedBytes);
        var secure = new SecureString();
        foreach (var c in hex) secure.AppendChar(c);
        secure.MakeReadOnly();
        
        Array.Clear(seedBytes);
        return secure;
    }

    private static byte[] DeriveSessionKeyFromSecureSeed(SecureString secureSeed)
    {
        byte[] hashedKey = new byte[32]; // 256-bit session key
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureSeed);
        try
        {
            string hex = Marshal.PtrToStringUni(ptr)!;
            byte[] seedBytes = Convert.FromHexString(hex);
            
            MonocypherNative.crypto_blake2b(hashedKey, (nuint)hashedKey.Length, seedBytes, (nuint)seedBytes.Length);
            
            Array.Clear(seedBytes);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
        return hashedKey;
    }

    public static async Task Main(string[] args)
    {
        // 0. Cleanup vaults on startup
        LuxVault.CleanupVaults();

        // 1. Generate 64-bit seed and wrap in SecureString
        using SecureString secureSeed = Generate64BitSecureSeed();
        
        // 2. Derive the 256-bit session key
        byte[] sessionKey = DeriveSessionKeyFromSecureSeed(secureSeed);
        
        // 3. Initialize the global vault security with this transient key
        LuxVault.InitializeSessionKey(sessionKey);
        ProtectedSecret.InitializeSessionKey(sessionKey);

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
                
                var serverConfig = new ServerConfig();
                serverConfigSection.Bind(serverConfig);
                
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

        Array.Clear(sessionKey);
        
        try {
            await host.RunAsync();
        }
        finally {
            // Cleanup vaults on shutdown
            LuxVault.CleanupVaults();
        }
    }
}

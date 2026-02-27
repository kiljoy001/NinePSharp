using System;
using System.IO;
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
using NinePSharp.Server.Backends.Cloud;
using NinePSharp.Backends.PowerShell;
using NinePSharp.Backends.Pipes;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Configuration.Parser;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

public class Program
{
    internal static void CleanupVaultsOnStartup() => LuxVault.CleanupVaults();
    internal static void CleanupVaultsOnShutdown() => LuxVault.CleanupVaults();

    private static SecureString Generate4096BitSecureSeed()
    {
        byte[] seedBytes = new byte[512]; // 4096 bits
        RandomNumberGenerator.Fill(seedBytes);
        
        var secure = new SecureString();
        // Zero-exposure: append bytes as chars without hex string conversion
        foreach (var b in seedBytes) secure.AppendChar((char)b);
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
            unsafe {
                byte* pSeed = (byte*)ptr.ToPointer();
                // Extract original bytes from the Unicode chars
                byte[] seedBytes = new byte[secureSeed.Length];
                for (int i = 0; i < secureSeed.Length; i++) {
                    seedBytes[i] = (byte)pSeed[i * 2]; // Unicode is 2 bytes per char
                }
                
                MonocypherNative.crypto_blake2b(hashedKey, (nuint)hashedKey.Length, seedBytes, (nuint)seedBytes.Length);
                Array.Clear(seedBytes);
            }
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
        return hashedKey;
    }

    public static async Task Main(string[] args)
    {
        // Apply OS-level hardening (Anti-Dumping)
        ProcessHardening.Apply();

        // 0. Cleanup vaults on startup
        CleanupVaultsOnStartup();

        // 1. Generate 4096-bit seed and wrap in SecureString
        using SecureString secureSeed = Generate4096BitSecureSeed();
        
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
                if (serverConfig.Secret != null) services.AddSingleton(serverConfig.Secret);
                if (serverConfig.Aws != null) services.AddSingleton(serverConfig.Aws);
                if (serverConfig.Azure != null) services.AddSingleton(serverConfig.Azure);
                if (serverConfig.Gcp != null) services.AddSingleton(serverConfig.Gcp);
                if (serverConfig.Cardano != null) services.AddSingleton(serverConfig.Cardano);
                if (serverConfig.PowerShell != null) services.AddSingleton(serverConfig.PowerShell);
                if (serverConfig.Pipes != null) services.AddSingleton(serverConfig.Pipes);
                if (serverConfig.Emercoin != null) 
                {
                    services.AddSingleton(serverConfig.Emercoin);
                    services.Configure<EmercoinConfig>(hostContext.Configuration.GetSection("Server:Emercoin"));
                }
                services.AddSingleton<SrvBackend>();

                services.AddHttpClient();
                services.AddSingleton<IEmercoinNvsClient, EmercoinNvsClient>();
                services.AddSingleton<IEmercoinAuthService, EmercoinAuthService>();

                services.AddSingleton<IClusterManager, ClusterManager>();
                services.AddSingleton<ILuxVaultService, LuxVaultService>();
                services.AddSingleton<IParser, ConfigParser>();
                
                if (serverConfig.Secret != null) services.AddSingleton<IProtocolBackend, SecretBackend>();
                if (serverConfig.Aws != null) services.AddSingleton<IProtocolBackend, AwsBackend>();
                if (serverConfig.Azure != null) services.AddSingleton<IProtocolBackend, AzureBackend>();
                if (serverConfig.Gcp != null) services.AddSingleton<IProtocolBackend, GcpBackend>();
                if (serverConfig.Cardano != null) services.AddSingleton<IProtocolBackend, CardanoBackend>();
                if (serverConfig.PowerShell != null) services.AddSingleton<IProtocolBackend, PowerShellBackend>();
                if (serverConfig.Pipes != null) services.AddSingleton<IProtocolBackend, PipeBackend>();
                services.AddSingleton<IProtocolBackend, SrvBackend>();

                services.AddSingleton<INinePFSDispatcher, NinePFSDispatcher>();
                services.AddHostedService<NinePServer>();
            })
            .Build();

        Array.Clear(sessionKey);
        
        try {
            await host.RunAsync();
        }
        finally {
            // Cleanup vaults on shutdown
            CleanupVaultsOnShutdown();
        }
    }
}

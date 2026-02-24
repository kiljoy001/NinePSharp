using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Utils;
using Xunit;
using FluentAssertions;
using Moq;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests
{
    public class ServerStartupTests
    {
        #region Unit Tests (Program.cs Logic)

        [Fact]
        public void Generate64BitSecureSeed_ProducesUniqueSeeds()
        {
            // Act
            using var seed1 = ReflectionHelper.InvokeStatic<SecureString>(typeof(Program), "Generate64BitSecureSeed");
            using var seed2 = ReflectionHelper.InvokeStatic<SecureString>(typeof(Program), "Generate64BitSecureSeed");

            // Assert
            seed1.Length.Should().Be(8);
            seed2.Length.Should().Be(8);
            
            string s1 = ToInsecureString(seed1);
            string s2 = ToInsecureString(seed2);
            s1.Should().NotBe(s2);
        }

        [Fact]
        public void DeriveSessionKey_ProducesCorrectLength()
        {
            // Arrange
            using var seed = ReflectionHelper.InvokeStatic<SecureString>(typeof(Program), "Generate64BitSecureSeed");

            // Act
            byte[] key = ReflectionHelper.InvokeStatic<byte[]>(typeof(Program), "DeriveSessionKeyFromSecureSeed", seed);

            // Assert
            key.Should().NotBeNull();
            key.Length.Should().Be(32);
            key.All(b => b == 0).Should().BeFalse();
        }

        #endregion

        #region Property Tests

        [Property]
        public bool DeriveSessionKey_IsDeterministic(byte[] seedData)
        {
            if (seedData == null || seedData.Length == 0) return true;
            
            byte[] normalized = new byte[8];
            Array.Copy(seedData, 0, normalized, 0, Math.Min(seedData.Length, 8));

            using var secureSeed1 = new SecureString();
            using var secureSeed2 = new SecureString();
            foreach (var b in normalized) 
            {
                secureSeed1.AppendChar((char)b);
                secureSeed2.AppendChar((char)b);
            }
            secureSeed1.MakeReadOnly();
            secureSeed2.MakeReadOnly();

            byte[] key1 = ReflectionHelper.InvokeStatic<byte[]>(typeof(Program), "DeriveSessionKeyFromSecureSeed", secureSeed1);
            byte[] key2 = ReflectionHelper.InvokeStatic<byte[]>(typeof(Program), "DeriveSessionKeyFromSecureSeed", secureSeed2);

            return StructuralComparisons.StructuralEqualityComparer.Equals(key1, key2);
        }

        #endregion

        #region DI Smoke Test

        [Fact]
        public void DI_Container_Can_Resolve_NinePServer_And_Dependencies()
        {
            // Arrange
            var args = Array.Empty<string>();
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, config) => {
                    config.AddInMemoryCollection(new Dictionary<string, string?> {
                        ["Server:Endpoints:0:Address"] = "127.0.0.1",
                        ["Server:Endpoints:0:Port"] = "9999",
                        ["Server:Endpoints:0:Protocol"] = "tcp"
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var serverConfig = new ServerConfig { 
                        Endpoints = new List<EndpointConfig> { 
                            new EndpointConfig { Address = "127.0.0.1", Port = 9999, Protocol = "tcp" } 
                        } 
                    };
                    services.AddSingleton(Options.Create(serverConfig));
                    services.AddSingleton(serverConfig);
                    
                    services.AddLogging();
                    services.AddSingleton<IClusterManager, ClusterManager>();
                    services.AddSingleton<INinePFSDispatcher, NinePFSDispatcher>();
                    services.AddSingleton<IEmercoinAuthService, EmercoinAuthService>();
                    services.AddSingleton<IEmercoinNvsClient, EmercoinNvsClient>();
                    services.AddHttpClient();
                    
                    services.AddSingleton<IEnumerable<IProtocolBackend>>(new List<IProtocolBackend>());
                    
                    services.AddHostedService<NinePServer>();
                });

            // Act & Assert
            using var host = hostBuilder.Build();
            var server = host.Services.GetRequiredService<IHostedService>();
            server.Should().BeOfType<NinePServer>();
        }

        #endregion

        #region Fuzz/Integration Test

        [Fact]
        public async Task NinePServer_Handles_Malformed_Connection_Gracefully()
        {
            int port = GetFreePort();
            var cts = new CancellationTokenSource();
            
            var logger = new Mock<ILogger<NinePServer>>();
            var config = Options.Create(new ServerConfig {
                Endpoints = new List<EndpointConfig> {
                    new EndpointConfig { Address = "127.0.0.1", Port = port, Protocol = "tcp" }
                }
            });
            
            var dispatcher = new Mock<INinePFSDispatcher>();
            var cluster = new Mock<IClusterManager>();
            var configuration = new ConfigurationBuilder().Build();
            var auth = new Mock<IEmercoinAuthService>();

            var server = new NinePServer(logger.Object, config, new List<IProtocolBackend>(), dispatcher.Object, cluster.Object, configuration, auth.Object);

            var serverTask = server.StartAsync(cts.Token);

            try {
                await Task.Delay(500);

                var random = new System.Random();
                for (int i = 0; i < 50; i++)
                {
                    try {
                        using var client = new TcpClient();
                        await client.ConnectAsync("127.0.0.1", port);
                        using var stream = client.GetStream();
                        
                        byte[] junk = new byte[random.Next(1, 512)];
                        random.NextBytes(junk);
                        
                        await stream.WriteAsync(junk);
                        await Task.Delay(5);
                    } catch { /* Ignore client-side connection errors during fuzzing */ }
                }
            }
            finally {
                cts.Cancel();
                try { await serverTask; } catch (OperationCanceledException) { }
            }

            serverTask.IsFaulted.Should().BeFalse();
        }

        #endregion

        #region Helpers

        private static string ToInsecureString(SecureString s)
        {
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(s);
            try {
                return Marshal.PtrToStringUni(ptr)!;
            }
            finally {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static class ReflectionHelper
        {
            public static T InvokeStatic<T>(Type type, string methodName, params object[] args)
            {
                var method = type.GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method == null) throw new Exception($"Method {methodName} not found on {type.Name}");
                return (T)method.Invoke(null, args)!;
            }
        }

        #endregion
    }
}

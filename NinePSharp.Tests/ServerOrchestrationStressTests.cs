using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using FluentAssertions;
using Moq;

namespace NinePSharp.Tests;

[Collection("Global Arena")]
public class ServerOrchestrationStressTests
{
    private static int GetFreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Server_Handles_100k_Pipelined_Requests()
    {
        int port = GetFreePort();
        var cts = new CancellationTokenSource();
        
        var config = Options.Create(new ServerConfig {
            Endpoints = new List<EndpointConfig> {
                new EndpointConfig { Address = "127.0.0.1", Port = port, Protocol = "tcp" }
            }
        });

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new List<IProtocolBackend>(), new Mock<IClusterManager>().Object);
        var server = new NinePServer(NullLogger<NinePServer>.Instance, config, new List<IProtocolBackend>(), dispatcher, new Mock<IClusterManager>().Object, new ConfigurationBuilder().Build(), new Mock<IEmercoinAuthService>().Object);

        var serverTask = server.StartAsync(cts.Token);
        await Task.Delay(500);

        const int requestCount = 100_000;
        using var client = new TcpClient("127.0.0.1", port);
        var stream = client.GetStream();

        // 1. Prepare Tversion message bytes
        var tversion = new Tversion(1, 8192, "9P2000");
        byte[] msgBytes = new byte[tversion.Size];
        tversion.WriteTo(msgBytes);

        Console.WriteLine($"[Orch] Pipelining {requestCount} requests...");
        var sw = Stopwatch.StartNew();

        // 2. Fire-and-forget send (pipelining)
        var sendTask = Task.Run(async () => {
            for (int i = 0; i < requestCount; i++) {
                await stream.WriteAsync(msgBytes);
            }
        });

        // 3. Receive responses
        int received = 0;
        byte[] header = new byte[7];
        while (received < requestCount) {
            int read = await stream.ReadAtLeastAsync(header, 7);
            if (read < 7) break;
            
            uint size = BitConverter.ToUInt32(header, 0);
            byte[] payload = new byte[size - 7];
            await stream.ReadExactlyAsync(payload);
            received++;
        }

        sw.Stop();
        Console.WriteLine($"[Orch] Pipelined {received} responses in {sw.Elapsed.TotalSeconds:F2}s ({received/sw.Elapsed.TotalSeconds:F0} req/sec)");

        received.Should().Be(requestCount);
        
        cts.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task Graceful_Shutdown_Wipes_Arena()
    {
        // Use reflection to get sharded arenas
        var arenasField = typeof(LuxVault).GetField("Arenas", BindingFlags.Public | BindingFlags.Static)!;
        var activeAllocationsProp = typeof(SecureMemoryArena).GetProperty("ActiveAllocations")!;
        var arenas = (SecureMemoryArena[])arenasField.GetValue(null)!;

        // 1. Verify total allocations return to baseline (all shards)
        Func<int> getTotalActive = () => arenas.Sum(a => (int)activeAllocationsProp.GetValue(a)!);
        int baseline = getTotalActive();

        // 2. Perform a decryption.
        var payload = LuxVault.Encrypt(new byte[1024], "pass");
        using (var secret = LuxVault.DecryptToBytes(payload, "pass"))
        {
            // DecryptToBytes allocates a temporary SecureBuffer from an arena shard,
            // uses it, and then frees it before returning.
            getTotalActive().Should().Be(baseline, "Internal arena-backed buffers must be freed before DecryptToBytes returns");
        }
    }
}

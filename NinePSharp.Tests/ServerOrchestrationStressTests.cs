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
        // Use reflection to get arena baseline
        var arenaField = typeof(LuxVault).GetField("Arena", BindingFlags.NonPublic | BindingFlags.Static)!;
        var activeAllocationsProp = arenaField.FieldType.GetProperty("ActiveAllocations")!;
        var arenaInstance = arenaField.GetValue(null)!;

        // 1. Verify arena is clean initially
        int baseline = (int)activeAllocationsProp.GetValue(arenaInstance)!;

        // 2. Perform a decryption. 
        // LuxVault internal methods allocate a SecureBuffer from the arena,
        // use it for decryption, and then copy the result to a pinned array
        // BEFORE disposing the SecureBuffer (which frees it back to the arena).
        var payload = LuxVault.Encrypt(new byte[1024], "pass");
        using (var secret = LuxVault.DecryptToBytes(payload, "pass"))
        {
            // At this point, DecryptInternal has already returned.
            // Its internal SecureBuffer (arena-backed) should already be freed.
            // The 'secret' object itself is a separate pinned array managed by GC.
            int active = (int)activeAllocationsProp.GetValue(arenaInstance)!;
            active.Should().Be(baseline, "Internal arena-backed buffers should be freed before method returns");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Client.Tests;

public class ClientTests
{
    [Fact]
    public async Task VersionAsync_Works()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        var serverTask = Task.Run(async () =>
        {
            try {
                byte[] header = new byte[NinePConstants.HeaderSize];
                await serverStream.ReadExactlyAsync(header, default);
                var tag = BitConverter.ToUInt16(header, 5);
                
                var rversion = new Rversion(tag, 8192, "9P2000.L");
                byte[] response = new byte[rversion.Size];
                rversion.WriteTo(response);
                await serverStream.WriteAsync(response, default);
                await serverStream.FlushAsync();
            } catch {}
        });

        var result = await client.VersionAsync(8192, "9P2000.L");
        Assert.Equal(8192u, result.MSize);
    }

    [Fact]
    public async Task ConcurrentRequests_AreMultiplexedCorrectly()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        var serverTask = Task.Run(async () =>
        {
            try {
                var pendingResponses = new List<byte[]>();
                for (int i = 0; i < 2; i++)
                {
                    byte[] header = new byte[NinePConstants.HeaderSize];
                    await serverStream.ReadExactlyAsync(header, default);
                    uint size = BitConverter.ToUInt32(header, 0);
                    ushort tag = BitConverter.ToUInt16(header, 5);
                    
                    byte[] payload = new byte[size - NinePConstants.HeaderSize];
                    if (payload.Length > 0) await serverStream.ReadExactlyAsync(payload, default);

                    var rclunk = new Rclunk(tag);
                    byte[] response = new byte[rclunk.Size];
                    rclunk.WriteTo(response);
                    pendingResponses.Add(response);
                }

                // Send responses in reverse order
                await serverStream.WriteAsync(pendingResponses[1], default);
                await serverStream.WriteAsync(pendingResponses[0], default);
                await serverStream.FlushAsync();
            } catch {}
        });

        var task1 = client.ClunkAsync(100);
        var task2 = client.ClunkAsync(200);

        await Task.WhenAll(task1, task2);

        Assert.True(task1.IsCompletedSuccessfully);
        Assert.True(task2.IsCompletedSuccessfully);
    }
}

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
    public async Task WstatAsync_Sends_The_Fid_And_Stat_And_Returns_Rwstat()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);
        var renamed = new Stat(0, ushort.MaxValue, uint.MaxValue, new Qid(QidType.QTDIR, uint.MaxValue, ulong.MaxValue),
            uint.MaxValue, uint.MaxValue, uint.MaxValue, ulong.MaxValue, "glenda2", "", "", "");

        Task<Twstat> serverTask = Task.Run(async () =>
        {
            byte[] header = new byte[NinePConstants.HeaderSize];
            await serverStream.ReadExactlyAsync(header, default);
            uint size = BitConverter.ToUInt32(header, 0);
            byte[] message = new byte[size];
            header.CopyTo(message, 0);
            await serverStream.ReadExactlyAsync(message.AsMemory(NinePConstants.HeaderSize), default);
            var request = new Twstat(message);
            var reply = new Rwstat(request.Tag);
            byte[] response = new byte[reply.Size];
            reply.WriteTo(response);
            await serverStream.WriteAsync(response, default);
            await serverStream.FlushAsync();
            return request;
        });

        Rwstat result = await client.WstatAsync(7, renamed);
        Twstat sent = await serverTask;

        Assert.Equal(7u, sent.Fid);
        Assert.Equal("glenda2", sent.Stat.Name);
        Assert.Equal(sent.Tag, result.Tag);
    }

    [Fact]
    public async Task AuthAsync_Sends_The_Afid_Uname_And_Aname_And_Returns_Rauth()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);
        var authQid = new Qid(QidType.QTAUTH, 0, 42);

        Task<Tauth> serverTask = Task.Run(async () =>
        {
            byte[] header = new byte[NinePConstants.HeaderSize];
            await serverStream.ReadExactlyAsync(header, default);
            uint size = BitConverter.ToUInt32(header, 0);
            byte[] message = new byte[size];
            header.CopyTo(message, 0);
            await serverStream.ReadExactlyAsync(message.AsMemory(NinePConstants.HeaderSize), default);
            var request = new Tauth(message);
            var reply = new Rauth(request.Tag, authQid);
            byte[] response = new byte[reply.Size];
            reply.WriteTo(response);
            await serverStream.WriteAsync(response, default);
            await serverStream.FlushAsync();
            return request;
        });

        Rauth result = await client.AuthAsync(5, "glenda", "keys");
        Tauth sent = await serverTask;

        Assert.Equal(5u, sent.Afid);
        Assert.Equal("glenda", sent.Uname);
        Assert.Equal("keys", sent.Aname);
        Assert.Equal(sent.Tag, result.Tag);
        Assert.Equal(42ul, result.Aqid.Path);
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

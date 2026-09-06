using System.Net;
using System.Net.Sockets;
using System.Text;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Messages;
using Xunit;

namespace NinePSharp.Client.Tests;

public class MultiplexingRobustnessTests
{
    [Fact]
    public async Task Client_HandlesRerror_ByThrowingNinePException()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        var serverTask = Task.Run(async () =>
        {
            byte[] header = new byte[NinePConstants.HeaderSize];
            await serverStream.ReadExactlyAsync(header);
            ushort tag = BitConverter.ToUInt16(header, 5);

            var rerror = new Rerror(tag, "Simulated Error");
            byte[] resp = new byte[rerror.Size];
            rerror.WriteTo(resp);
            await serverStream.WriteAsync(resp);
        });

        var ex = await Assert.ThrowsAsync<NinePException>(() => client.ClunkAsync(1));
        Assert.Equal("Simulated Error", ex.Message);
    }

    [Fact]
    public async Task Client_HandlesRlerror_ByThrowingNinePException()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        var serverTask = Task.Run(async () =>
        {
            byte[] header = new byte[NinePConstants.HeaderSize];
            await serverStream.ReadExactlyAsync(header);
            ushort tag = BitConverter.ToUInt16(header, 5);

            var rlerror = new Rlerror(tag, 22); // EINVAL
            byte[] resp = new byte[rlerror.Size];
            rlerror.WriteTo(resp);
            await serverStream.WriteAsync(resp);
        });

        var ex = await Assert.ThrowsAsync<NinePException>(() => client.ClunkAsync(1));
        Assert.Contains("22", ex.Message);
    }

    [Fact]
    public async Task Client_HandlesUnexpectedMessage_ByFailingPendingRequests()
    {
        var (clientStream, serverStream) = LoopbackStream.CreatePair();
        using var client = new NinePClient(clientStream);

        var serverTask = Task.Run(async () =>
        {
            byte[] header = new byte[NinePConstants.HeaderSize];
            await serverStream.ReadExactlyAsync(header);
            ushort tag = BitConverter.ToUInt16(header, 5);

            // Send a valid message but with an unsupported type code (e.g., 255)
            byte[] resp = new byte[NinePConstants.HeaderSize];
            BitConverter.TryWriteBytes(resp, (uint)NinePConstants.HeaderSize);
            resp[4] = 255;
            BitConverter.TryWriteBytes(resp.AsSpan(5), tag);

            await serverStream.WriteAsync(resp);
        });

        await Assert.ThrowsAsync<NotSupportedException>(() => client.ClunkAsync(1));
    }
}

using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NinePSharp.Client;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class GatewayListenerTests
{
    [Fact]
    public async Task StopReleasesPortAndWaitsForProviderCleanupBeforeCompleting()
    {
        var test = new GatewayTestContext();
        var clunkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Resources.Setup(value => value.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (ResourceOpenHandle handle, ResourceOperationContext context, CancellationToken token) =>
            {
                clunkStarted.TrySetResult();
                await releaseClunk.Task;
            });
        var options = new NinePOrleansListenerOptions();
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(test.Dispatcher,
            Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        var endpoint = listener.LocalEndpoint;
        using var client = new NinePClient("127.0.0.1", endpoint.Port);
        await client.VersionAsync(512, "9P2000");
        await client.AttachAsync(1, NinePConstants.NoFid, "user", "/");
        await client.WalkAsync(1, 2, new[] { "file" });
        await client.OpenAsync(2, NinePConstants.OREAD);
        Task stopping = listener.StopAsync(CancellationToken.None);
        try
        {
            await clunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task first = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.NotSame(stopping, first);
        }
        finally
        {
            releaseClunk.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }

        using var next = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => next.ConnectAsync(endpoint).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DisposeStopsAcceptingAndDisconnectsClientsWithoutExplicitStop()
    {
        var options = new NinePOrleansListenerOptions();
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(new GatewayTestContext().Dispatcher,
            Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        var endpoint = listener.LocalEndpoint;
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint);
        await SendAsync(client.GetStream(), new Tversion(65535, 512, "9P2000"));
        await ReceiveAsync(client.GetStream());
        listener.Dispose();
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        await listener.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        using var next = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => next.ConnectAsync(endpoint).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AdmissionLimitRejectsExcessClientsAndShutdownClosesIdleConnections()
    {
        var options = new NinePOrleansListenerOptions { MaxConnections = 1 };
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(new GatewayTestContext().Dispatcher,
            Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        using var first = new TcpClient();
        await first.ConnectAsync(listener.LocalEndpoint);
        await SendAsync(first.GetStream(), new Tversion(65535, 512, "9P2000"));
        Assert.Equal(MessageTypes.Rversion, (MessageTypes)(await ReceiveAsync(first.GetStream()))[4]);
        using var excess = new TcpClient();
        await excess.ConnectAsync(listener.LocalEndpoint);
        Assert.Equal(0, await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(listener.ExecuteTask!.IsFaulted);
        await listener.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task StartupIsLoggedAndAcceptedClientsDisableNagleDelay()
    {
        using var accepted = new TcpClient();
        Assert.False(accepted.NoDelay);
        Assert.Same(accepted, NinePOrleansListener.ConfigureAcceptedClient(accepted));
        Assert.True(accepted.NoDelay);

        var logger = new Mock<ILogger<NinePOrleansListener>>();
        var options = new NinePOrleansListenerOptions();
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(
            new GatewayTestContext().Dispatcher,
            Options.Create(options),
            logger.Object);
        await listener.StartAsync(CancellationToken.None);
        try
        {
            Assert.Contains(logger.Invocations, invocation =>
                invocation.Method.Name == nameof(ILogger.Log)
                && invocation.Arguments[0] is LogLevel.Information);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task LinuxMessagesTraverseTheActualFramerAndDispatcher()
    {
        var options = new NinePOrleansListenerOptions();
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(new GatewayTestContext().Dispatcher,
            Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        try
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(listener.LocalEndpoint);
            var stream = connection.GetStream();
            await SendAsync(stream, new Tversion(65535, 512, "9P2000.L"));
            Assert.Equal("9P2000.L", new Rversion(await ReceiveAsync(stream)).Version);
            await SendAsync(stream, new Tattach(1, 1, NinePConstants.NoFid, "user", "/", uint.MaxValue));
            byte[] attached = await ReceiveAsync(stream);
            Assert.True((MessageTypes)attached[4] == MessageTypes.Rattach,
                (MessageTypes)attached[4] == MessageTypes.Rerror ? new Rerror(attached).Ename : "unexpected attach reply");
            await SendAsync(stream, new Twalk(2, 1, 2, new[] { "file" }));
            Assert.Single(new Rwalk(await ReceiveAsync(stream)).Wqid);
            await SendAsync(stream, new Tlopen(15, 3, 2, 0));
            byte[] opened = await ReceiveAsync(stream);
            Assert.Equal(MessageTypes.RLopen, (MessageTypes)opened[4]);
            Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(opened.AsSpan(5)));
            await SendAsync(stream, new Tread(4, 2, 0, uint.MaxValue));
            Assert.Equal(501, new Rread(await ReceiveAsync(stream)).Data.Length);
            await SendAsync(stream, new Tstatfs(11, 5, 1));
            Assert.Equal((uint)LinuxErrorCode.EOPNOTSUPP, new Rlerror(await ReceiveAsync(stream)).Ecode);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DisconnectedClientReleasesAdmissionSlot()
    {
        var options = new NinePOrleansListenerOptions { MaxConnections = 1 };
        options.Endpoint.Port = 0;
        using var listener = new NinePOrleansListener(new GatewayTestContext().Dispatcher,
            Options.Create(options), NullLogger<NinePOrleansListener>.Instance);
        await listener.StartAsync(CancellationToken.None);
        try
        {
            using (var client = new NinePClient("127.0.0.1", listener.LocalEndpoint.Port))
            {
                await client.VersionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                using var client = new NinePClient("127.0.0.1", listener.LocalEndpoint.Port);
                try
                {
                    await client.VersionAsync().WaitAsync(deadline.Token);
                    break;
                }
                catch (Exception error) when (error is IOException or NinePException)
                {
                    await Task.Delay(10, deadline.Token);
                }
            }
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task SendAsync(Stream stream, ISerializable message)
    {
        byte[] data = new byte[message.Size];
        message.WriteTo(data);
        await stream.WriteAsync(data);
    }

    private static async Task<byte[]> ReceiveAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, timeout.Token);
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header));
        Assert.InRange(length, 7, 8192);
        byte[] data = new byte[length];
        header.CopyTo(data, 0);
        await stream.ReadExactlyAsync(data.AsMemory(7), timeout.Token);
        return data;
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NinePSharp.Server;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Namespaces.Orleans.Server;

/// <summary>Endpoint and admission settings for a hosted 9P listener.</summary>
public sealed class NinePOrleansListenerOptions
{
    /// <summary>Gets or sets the TCP or mutually authenticated TLS endpoint.</summary>
    public EndpointConfig Endpoint { get; set; } = new() { Address = "127.0.0.1", Port = 5640, Protocol = "tcp" };

    /// <summary>Gets or sets the maximum number of concurrently connected clients.</summary>
    public int MaxConnections { get; set; } = 256;
}

/// <summary>Hosts the distributed dispatcher with the shared 9P stream processor.</summary>
public sealed class NinePOrleansListener : BackgroundService
{
    private readonly ConcurrentDictionary<TcpClient, Task> connections = new();
    private readonly NinePConnectionProcessor processor;
    private readonly ILogger<NinePOrleansListener> logger;
    private readonly EndpointConfig endpoint;
    private readonly int maxConnections;
    private readonly TcpListener listener;

    /// <summary>Creates a listener; no socket is opened until the host starts.</summary>
    public NinePOrleansListener(
        DistributedNamespaceDispatcher dispatcher,
        IOptions<NinePOrleansListenerOptions> options,
        ILogger<NinePOrleansListener> logger,
        INinePTransportSecurity? security = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.logger = logger;
        NinePOrleansListenerOptions settings = options.Value;
        EndpointConfig configured = settings.Endpoint ?? throw new ArgumentException("An endpoint is required.", nameof(options));
        if (settings.MaxConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConnections must be positive.");
        }

        if (configured.Protocol is not ("tcp" or "tls"))
        {
            throw new ArgumentException("The endpoint protocol must be tcp or tls.", nameof(options));
        }

        if (configured.Protocol == "tls" && string.IsNullOrWhiteSpace(configured.ServerCertificatePath))
        {
            throw new ArgumentException("TLS requires a server certificate path.", nameof(options));
        }

        endpoint = new EndpointConfig
        {
            Address = configured.Address,
            Port = configured.Port,
            Protocol = configured.Protocol,
            ServerCertificatePath = configured.ServerCertificatePath,
            ServerCertificatePassword = configured.ServerCertificatePassword,
        };
        maxConnections = settings.MaxConnections;
        listener = new TcpListener(IPAddress.Parse(endpoint.Address), endpoint.Port);
        processor = new NinePConnectionProcessor(logger, dispatcher, security);
    }

    /// <summary>Gets the bound endpoint after startup, including the assigned port when configured with zero.</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)listener.LocalEndpoint;

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        listener.Start();
        logger.LogInformation("Orleans 9P endpoint listening on {Endpoint} ({Protocol})", LocalEndpoint, endpoint.Protocol);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (connections.Count >= maxConnections)
                {
                    client.Dispose();
                    continue;
                }

                client.NoDelay = true;
                Task processing = processor.HandleClientAsync(client, endpoint, stoppingToken);
                connections.TryAdd(client, processing);
                _ = RemoveCompletedAsync(client, processing);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            KeyValuePair<TcpClient, Task>[] active = connections.ToArray();
            foreach (var connection in active)
            {
                connection.Key.Dispose();
            }

            await Task.WhenAll(active.Select(static connection => connection.Value));
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
        listener.Stop();
        foreach (TcpClient client in connections.Keys)
        {
            client.Dispose();
        }
    }

    private async Task RemoveCompletedAsync(TcpClient client, Task processing)
    {
        try
        {
            await processing;
        }
        finally
        {
            connections.TryRemove(client, out _);
        }
    }
}

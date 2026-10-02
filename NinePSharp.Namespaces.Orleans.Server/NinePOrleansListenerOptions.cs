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

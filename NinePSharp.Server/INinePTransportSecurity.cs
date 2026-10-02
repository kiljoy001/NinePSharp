using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server;

public interface INinePTransportSecurity
{
    Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct);
}

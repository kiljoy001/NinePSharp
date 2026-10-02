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

public sealed class DefaultNinePTransportSecurity : INinePTransportSecurity
{
    public async Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct)
    {
        if (!endpoint.Protocol.Equals("tls", StringComparison.OrdinalIgnoreCase))
        {
            return new TransportSecurityResult(transport, null);
        }

        if (string.IsNullOrWhiteSpace(endpoint.ServerCertificatePath))
        {
            throw new InvalidOperationException("TLS endpoints require a configured server certificate path.");
        }

        using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            endpoint.ServerCertificatePath,
            endpoint.ServerCertificatePassword,
            X509KeyStorageFlags.DefaultKeySet,
            Pkcs12LoaderLimits.Defaults);
        var sslStream = new SslStream(transport, false);
        try
        {
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = serverCertificate,
                ClientCertificateRequired = true,
            };
            await sslStream.AuthenticateAsServerAsync(options, ct);

            return new TransportSecurityResult(sslStream, sslStream.RemoteCertificate as X509Certificate2);
        }
        catch
        {
            await sslStream.DisposeAsync();
            throw;
        }
    }
}

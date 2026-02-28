using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
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
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

internal interface INinePTransportSecurity
{
    Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct);
}

internal readonly record struct TransportSecurityResult(Stream Stream, X509Certificate2? ClientCertificate);

internal sealed class DefaultNinePTransportSecurity : INinePTransportSecurity
{
    public async Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct)
    {
        if (!endpoint.Protocol.Equals("tls", StringComparison.OrdinalIgnoreCase))
        {
            return new TransportSecurityResult(transport, null);
        }

        var sslStream = new SslStream(transport, false);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = true,
        }, ct);

        return new TransportSecurityResult(sslStream, sslStream.RemoteCertificate as X509Certificate2);
    }
}

internal sealed class NinePConnectionProcessor
{
    private readonly ILogger _logger;
    private readonly INinePFSDispatcher _dispatcher;
    private readonly IEmercoinAuthService _authService;
    private readonly INinePTransportSecurity _transportSecurity;

    internal NinePConnectionProcessor(
        ILogger logger,
        INinePFSDispatcher dispatcher,
        IEmercoinAuthService authService,
        INinePTransportSecurity? transportSecurity = null)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _authService = authService;
        _transportSecurity = transportSecurity ?? new DefaultNinePTransportSecurity();
    }

    internal sealed class ClientSession
    {
        public ClientSession()
        {
            State = TransportSessionOps.create(Guid.NewGuid().ToString("N"), NinePDialect.NineP2000, null);
        }

        public TransportSession State { get; set; }
        public string SessionId => State.Protocol.SessionId;
        public uint MSize => State.MSize;
        public NinePDialect Dialect
        {
            get => State.Protocol.Dialect;
            set => State = TransportSessionOps.withTransport(value, TransportSessionOps.certificateOrNull(State), State);
        }

        public X509Certificate2? ClientCertificate => TransportSessionOps.certificateOrNull(State);
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    internal async Task HandleClientAsync(TcpClient client, EndpointConfig endpoint, CancellationToken ct)
    {
        EndPoint? endPoint = client.Client.RemoteEndPoint;
        _logger.LogInformation("Client connected from {EndPoint}", endPoint);

        var session = new ClientSession();
        try
        {
            Stream transport = await AuthenticateTransportAsync(client.GetStream(), endpoint, session, endPoint, ct);
            await using (transport)
            {
                await ProcessStreamAsync(transport, endPoint, session, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client session for {EndPoint}", endPoint);
        }
        finally
        {
            client.Close();
        }
    }

    internal async Task<Stream> AuthenticateTransportAsync(Stream transport, EndpointConfig endpoint, ClientSession session, EndPoint? endPoint, CancellationToken ct)
    {
        var secured = await _transportSecurity.AuthenticateAsync(transport, endpoint, ct);
        if (secured.ClientCertificate is X509Certificate2 certificate)
        {
            session.State = TransportSessionOps.withTransport(session.State.Protocol.Dialect, certificate, session.State);
            bool authorized = await _authService.IsCertificateAuthorizedAsync(certificate);
            if (!authorized)
            {
                _logger.LogWarning("Client {EndPoint} failed Emercoin authorization.", endPoint);
            }
        }

        return secured.Stream;
    }

    internal async Task ProcessStreamAsync(Stream stream, EndPoint? endPoint, ClientSession session, CancellationToken ct)
    {
        var headerBuffer = new byte[NinePConstants.HeaderSize];

        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, headerBuffer, endPoint, session, ct);
            if (frame == null)
            {
                break;
            }

            try
            {
                _logger.LogInformation("Incoming: size={Size}, type={Type}, tag={Tag}", frame.Value.Size, frame.Value.Type, frame.Value.Tag);

                object response = await DispatchMessageAsync(frame.Value.Buffer.AsMemory(0, frame.Value.Size), frame.Value.Type, frame.Value.Tag, session);
                await SendResponseAsync(stream, response, session.WriteLock, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during dispatch");
                await SendResponseAsync(stream, new Rerror(frame.Value.Tag, ex.Message), session.WriteLock, ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame.Value.Buffer, clearArray: true);
            }
        }
    }

    internal async Task<object> DispatchMessageAsync(ReadOnlyMemory<byte> fullMessageBuffer, MessageTypes type, ushort tag, ClientSession session)
    {
        _ = type;
        var parsed = TransportSessionOps.parseMessage(fullMessageBuffer, tag, session.State);
        session.State = parsed.Session;

        if (parsed.ErrorResponse != null)
        {
            return parsed.ErrorResponse;
        }

        object response = await _dispatcher.DispatchAsync(
            session.State.Protocol.SessionId,
            parsed.Message,
            session.State.Protocol.Dialect,
            TransportSessionOps.certificateOrNull(session.State));

        var outcome = TransportSessionOps.applyResponse(response, session.State);
        session.State = outcome.Session;

        if (response is Rversion version)
        {
            _logger.LogInformation("Negotiated MSize: {MSize}, Dialect: {Dialect}, Version: {Version}", session.State.MSize, session.State.Protocol.Dialect, version.Version);
        }

        return outcome.Response;
    }

    internal async Task SendResponseAsync(Stream stream, object response, SemaphoreSlim writeLock, CancellationToken ct)
    {
        if (response is not ISerializable serializable)
        {
            return;
        }

        byte[] outBuffer = new byte[serializable.Size];
        serializable.WriteTo(outBuffer);

        await writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(outBuffer, ct);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task<FrameBuffer?> ReadFrameAsync(Stream stream, byte[] headerBuffer, EndPoint? endPoint, ClientSession session, CancellationToken ct)
    {
        int headerRead = await stream.ReadAtLeastAsync(headerBuffer, headerBuffer.Length, throwOnEndOfStream: false, ct);
        if (headerRead == 0)
        {
            _logger.LogInformation("Client {EndPoint} disconnected cleanly.", endPoint);
            return null;
        }

        if (headerRead < headerBuffer.Length)
        {
            _logger.LogWarning("Client {EndPoint} sent partial header ({HeaderRead} bytes).", endPoint, headerRead);
            return null;
        }

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(0, 4));
        if (size < NinePConstants.HeaderSize)
        {
            _logger.LogError("Invalid message size {Size} from {EndPoint}", size, endPoint);
            return null;
        }

        if (size > session.State.MSize)
        {
            _logger.LogError("Message size {Size} exceeds negotiated msize {MSize} from {EndPoint}", size, session.State.MSize, endPoint);
            return null;
        }

        int frameSize;
        try
        {
            frameSize = checked((int)size);
        }
        catch (OverflowException)
        {
            _logger.LogError("Message size {Size} from {EndPoint} exceeds supported frame bounds", size, endPoint);
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(frameSize);
        headerBuffer.CopyTo(buffer, 0);

        uint payloadSize = size - (uint)NinePConstants.HeaderSize;
        if (payloadSize > 0)
        {
            int payloadRead = await stream.ReadAtLeastAsync(buffer.AsMemory(NinePConstants.HeaderSize, (int)payloadSize), (int)payloadSize, throwOnEndOfStream: false, ct);
            if (payloadRead < payloadSize)
            {
                _logger.LogWarning("Client {EndPoint} disconnected mid-frame after {PayloadRead}/{PayloadSize} payload bytes.", endPoint, payloadRead, payloadSize);
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                return null;
            }
        }

        return new FrameBuffer(
            frameSize,
            (MessageTypes)headerBuffer[4],
            BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.AsSpan(5, 2)),
            buffer);
    }

    private readonly record struct FrameBuffer(int Size, MessageTypes Type, ushort Tag, byte[] Buffer);
}

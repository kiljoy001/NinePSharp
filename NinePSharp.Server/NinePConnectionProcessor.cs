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

public sealed class NinePConnectionProcessor
{
    private readonly ILogger logger;
    private readonly INinePFSDispatcher dispatcher;
    private readonly INinePTransportSecurity transportSecurity;
    private readonly ArrayPool<byte> buffers;

    public NinePConnectionProcessor(
        ILogger logger,
        INinePFSDispatcher dispatcher,
        INinePTransportSecurity? transportSecurity = null)
        : this(logger, dispatcher, transportSecurity, ArrayPool<byte>.Shared)
    {
    }

    public NinePConnectionProcessor(
        ILogger logger,
        INinePFSDispatcher dispatcher,
        INinePTransportSecurity? transportSecurity,
        ArrayPool<byte> buffers)
    {
        this.logger = logger;
        this.dispatcher = dispatcher;
        this.transportSecurity = transportSecurity ?? new DefaultNinePTransportSecurity();
        this.buffers = buffers ?? throw new ArgumentNullException(nameof(buffers));
    }

    // How long a connection that has ended waits for its session to close and its requests to finish.
    public TimeSpan Drain { get; init; } = TimeSpan.FromSeconds(5);

    public async Task HandleClientAsync(TcpClient client, EndpointConfig endpoint, CancellationToken ct)
    {
        EndPoint? endPoint = client.Client.RemoteEndPoint;
        logger.LogInformation("Client connected from {EndPoint}", endPoint);

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
            logger.LogError(ex, "Error handling client session for {EndPoint}", endPoint);
        }
        finally
        {
            client.Close();
        }
    }

    public async Task<Stream> AuthenticateTransportAsync(Stream transport, EndpointConfig endpoint, ClientSession session, EndPoint? endPoint, CancellationToken ct)
    {
        var secured = await transportSecurity.AuthenticateAsync(transport, endpoint, ct);
        if (secured.ClientCertificate is X509Certificate2 certificate)
        {
            session.State = TransportSessionOps.withTransport(session.State.Protocol.Dialect, certificate, session.State);
        }

        return secured.Stream;
    }

    public async Task ProcessStreamAsync(Stream stream, EndPoint? endPoint, ClientSession session, CancellationToken ct)
    {
        var headerBuffer = new byte[NinePConstants.HeaderSize];
        var work = new ConnectionWorkSet();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(stream, headerBuffer, endPoint, session, ct);
                if (frame == null)
                {
                    break;
                }

                // A tag is reusable only after its previous wire response. A duplicate cannot
                // safely receive Rerror (it would ambiguously answer the original operation).
                if (work.TryGetOutstanding(frame.Value.Tag, out _))
                {
                    buffers.Return(frame.Value.Buffer, clearArray: true);
                    break;
                }

                Task? precedingResponse = null;
                if (frame.Value.Type == MessageTypes.Tflush && frame.Value.Size == NinePConstants.HeaderSize + 2)
                {
                    ushort oldTag = BinaryPrimitives.ReadUInt16LittleEndian(frame.Value.Buffer.AsSpan(NinePConstants.HeaderSize, 2));
                    precedingResponse = work.FindResponse(oldTag);
                }
                else if (frame.Value.Type == MessageTypes.Tversion)
                {
                    precedingResponse = work.Barrier();
                }

                Task request = ProcessFrameAsync(stream, endPoint, session, frame.Value, ct, precedingResponse);
                work.Track(frame.Value.Tag, request);

                if (frame.Value.Type == MessageTypes.Tversion)
                {
                    await request;
                }
            }
        }
        finally
        {
            if (dispatcher is INinePSessionLifecycle lifecycle &&
                !await WithinDrainAsync(lifecycle.CloseSessionAsync(session.SessionId)))
            {
                logger.LogWarning("Closing the session of {EndPoint} did not finish within the drain limit; its outcome is unknown.", endPoint);
            }

            if (!await WithinDrainAsync(work.Barrier()))
            {
                logger.LogWarning("Requests from {EndPoint} did not finish within the drain limit ({Count} left); their outcome is unknown.", endPoint, work.PendingCount);
            }
        }
    }

    internal async Task<object> DispatchMessageAsync(ReadOnlyMemory<byte> fullMessageBuffer, MessageTypes type, ushort tag, ClientSession session)
    {
        _ = type;
        ParseOutcome parsed;
        lock (session.StateGate)
        {
            parsed = TransportSessionOps.parseMessage(fullMessageBuffer, tag, session.State);
            session.State = parsed.Session;
        }

        if (parsed.ErrorResponse != null)
        {
            return parsed.ErrorResponse;
        }

        object response = await dispatcher.DispatchAsync(
            session.State.Protocol.SessionId,
            parsed.Message,
            session.State.Protocol.Dialect,
            TransportSessionOps.certificateOrNull(session.State));

        ResponseOutcome outcome;
        lock (session.StateGate)
        {
            outcome = TransportSessionOps.applyResponse(response, session.State);
            session.State = outcome.Session;
        }

        if (response is Rversion version)
        {
            logger.LogInformation("Negotiated MSize: {MSize}, Dialect: {Dialect}, Version: {Version}", session.State.MSize, session.State.Protocol.Dialect, version.Version);
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

    private async Task<bool> WithinDrainAsync(Task work)
    {
        try
        {
            await work.WaitAsync(Drain);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task ProcessFrameAsync(
        Stream stream,
        EndPoint? endPoint,
        ClientSession session,
        FrameBuffer frame,
        CancellationToken cancellationToken,
        Task? precedingResponse)
    {
        try
        {
            logger.LogInformation(
                "Incoming: size={Size}, type={Type}, tag={Tag}",
                frame.Size,
                frame.Type,
                frame.Tag);

            object response = await DispatchMessageAsync(
                frame.Buffer.AsMemory(0, frame.Size),
                frame.Type,
                frame.Tag,
                session);

            // A flush/version reply is a wire barrier: older replies must already be sent.
            if (precedingResponse is not null)
            {
                await precedingResponse;
            }

            await SendResponseAsync(stream, response, session.WriteLock, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during dispatch for {EndPoint}", endPoint);
            try
            {
                await SendResponseAsync(
                    stream,
                    new Rerror(frame.Tag, ex.Message),
                    session.WriteLock,
                    cancellationToken);
            }
            catch (Exception sendError)
            {
                logger.LogDebug(sendError, "Could not send an error response to {EndPoint}", endPoint);
            }
        }
        finally
        {
            buffers.Return(frame.Buffer, clearArray: true);
        }
    }

    private async Task<FrameBuffer?> ReadFrameAsync(Stream stream, byte[] headerBuffer, EndPoint? endPoint, ClientSession session, CancellationToken ct)
    {
        int headerRead = await stream.ReadAtLeastAsync(headerBuffer, headerBuffer.Length, throwOnEndOfStream: false, ct);
        if (headerRead == 0)
        {
            logger.LogInformation("Client {EndPoint} disconnected cleanly.", endPoint);
            return null;
        }

        if (headerRead < headerBuffer.Length)
        {
            logger.LogWarning("Client {EndPoint} sent partial header ({HeaderRead} bytes).", endPoint, headerRead);
            return null;
        }

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(0, 4));
        if (size < NinePConstants.HeaderSize)
        {
            logger.LogError("Invalid message size {Size} from {EndPoint}", size, endPoint);
            return null;
        }

        if (size > session.State.MSize)
        {
            logger.LogError("Message size {Size} exceeds negotiated msize {MSize} from {EndPoint}", size, session.State.MSize, endPoint);
            return null;
        }

        int frameSize;
        try
        {
            frameSize = checked((int)size);
        }
        catch (OverflowException)
        {
            logger.LogError("Message size {Size} from {EndPoint} exceeds supported frame bounds", size, endPoint);
            return null;
        }

        var buffer = buffers.Rent(frameSize);
        headerBuffer.CopyTo(buffer, 0);

        uint payloadSize = size - (uint)NinePConstants.HeaderSize;
        try
        {
            int payloadRead = await stream.ReadAtLeastAsync(
                buffer.AsMemory(NinePConstants.HeaderSize, (int)payloadSize),
                (int)payloadSize,
                throwOnEndOfStream: false,
                ct);
            if (payloadRead < payloadSize)
            {
                logger.LogWarning("Client {EndPoint} disconnected mid-frame after {PayloadRead}/{PayloadSize} payload bytes.", endPoint, payloadRead, payloadSize);
                buffers.Return(buffer, clearArray: true);
                return null;
            }
        }
        catch
        {
            buffers.Return(buffer, clearArray: true);
            throw;
        }

        return new FrameBuffer(
            frameSize,
            (MessageTypes)headerBuffer[4],
            BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.AsSpan(5, 2)),
            buffer);
    }

    private readonly record struct FrameBuffer(int Size, MessageTypes Type, ushort Tag, byte[] Buffer);

    public sealed class ClientSession
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

        internal object StateGate { get; } = new();
    }

    internal sealed class ConnectionWorkSet
    {
        private readonly object gate = new();
        private readonly HashSet<Task> pending = new();
        private readonly Dictionary<ushort, Task> responsesByTag = new();

        internal int PendingCount
        {
            get
            {
                lock (gate)
                {
                    return pending.Count;
                }
            }
        }

        internal int ResponseCount
        {
            get
            {
                lock (gate)
                {
                    return responsesByTag.Count;
                }
            }
        }

        internal bool TryGetOutstanding(ushort tag, out Task? response)
        {
            lock (gate)
            {
                if (responsesByTag.TryGetValue(tag, out response) && !response.IsCompleted)
                {
                    return true;
                }

                response = null;
                return false;
            }
        }

        internal Task? FindResponse(ushort tag)
        {
            lock (gate)
            {
                return responsesByTag.GetValueOrDefault(tag);
            }
        }

        internal Task Barrier()
        {
            lock (gate)
            {
                return Task.WhenAll(pending);
            }
        }

        internal void Track(ushort tag, Task response)
        {
            lock (gate)
            {
                pending.Add(response);
                responsesByTag[tag] = response;
            }

            _ = ForgetWhenCompleteAsync(tag, response);
        }

        private async Task ForgetWhenCompleteAsync(ushort tag, Task response)
        {
            try
            {
                await response;
            }
            catch
            {
            }

            lock (gate)
            {
                pending.Remove(response);
                if (responsesByTag.TryGetValue(tag, out Task? current) && ReferenceEquals(current, response))
                {
                    responsesByTag.Remove(tag);
                }
            }
        }
    }
}

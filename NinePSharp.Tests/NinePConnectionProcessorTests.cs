using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Tests;

public class NinePConnectionProcessorTests
{
    [Fact]
    public async Task DuplicateInFlightTagTerminatesWithoutDispatchingOrAnsweringTheDuplicate()
    {
        var complete = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<INinePFSDispatcher>();
        int dispatched = 0;
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(() =>
            {
                dispatched++;
                return complete.Task;
            });
        var lifecycle = dispatcher.As<INinePSessionLifecycle>();
        lifecycle.Setup(value => value.CloseSessionAsync(It.IsAny<string>())).Returns(() =>
        {
            complete.TrySetResult(new Rread(7, new byte[] { 42 }));
            return Task.CompletedTask;
        });
        var buffers = new TrackingBufferPool();
        var processor = new NinePConnectionProcessor(NullLogger.Instance, dispatcher.Object, null, buffers);
        byte[] one = Serialize(new Tread(7, 1, 0, 1));
        using var stream = new ScriptedDuplexStream(one.Concat(one).Concat(Serialize(new Tread(8, 1, 0, 1))).ToArray());
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, dispatched);
        Assert.Equal(2, buffers.Rents);
        Assert.Equal(2, buffers.Returns);
        Assert.Equal(new[] { true, true }, buffers.ClearFlags);
        Assert.Equal(new[] { MessageTypes.Rread }, ResponseTypes(stream.Written.Span));
        Assert.Equal(new byte[] { 42 }, new Rread(stream.Written).Data.ToArray());
    }

    [Fact]
    public async Task ATagReusedAsSoonAsItsReplyIsOnTheWireIsServed()
    {
        var dispatcher = new Mock<INinePFSDispatcher>();
        int dispatched = 0;
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(() => Task.FromResult<object>(new Rread(7, new[] { (byte)Interlocked.Increment(ref dispatched) })));
        var processor = new NinePConnectionProcessor(NullLogger.Instance, dispatcher.Object);
        byte[] read = Serialize(new Tread(7, 1, 0, 1));
        using var stream = new TagReusingStream(read, read);
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, dispatched);
        Assert.Equal(new[] { MessageTypes.Rread, MessageTypes.Rread }, ResponseTypes(stream.Written.Span));
    }

    [Fact]
    public async Task DefaultTlsRejectsAnonymousClientAndClosesFailedTransport()
    {
        using var certificate = CreateSelfSignedCertificate();
        string path = Path.Combine(Path.GetTempPath(), $"ninep-tls-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pfx));
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var accepted = await listener.AcceptTcpClientAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            NetworkStream transport = accepted.GetStream();
            Task<TransportSecurityResult> server = new DefaultNinePTransportSecurity().AuthenticateAsync(
                transport,
                new EndpointConfig { Protocol = "tls", ServerCertificatePath = path },
                timeout.Token);
            using var ssl = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
            Task handshake = ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
            await Assert.ThrowsAsync<AuthenticationException>(() => server);
            _ = await Record.ExceptionAsync(() => handshake);
            Assert.False(transport.CanRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PartialHeaderNeverRentsAPayloadBuffer()
    {
        var buffers = new TrackingBufferPool();
        var processor = new NinePConnectionProcessor(NullLogger.Instance, new Mock<INinePFSDispatcher>().Object, null, buffers);
        using var stream = new ScriptedDuplexStream(new byte[] { 19, 0, 0 });
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None);
        Assert.Equal(0, buffers.Rents);
    }

    [Fact]
    public async Task FrameBeyondSupportedArrayBoundsClosesWithoutRenting()
    {
        var buffers = new TrackingBufferPool();
        var processor = new NinePConnectionProcessor(NullLogger.Instance, new Mock<INinePFSDispatcher>().Object, null, buffers);
        var session = new NinePConnectionProcessor.ClientSession();
        session.State = new NinePSharp.Core.FSharp.TransportSession(session.State.Protocol, uint.MaxValue);
        byte[] header = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(header, uint.MaxValue);
        using var stream = new ScriptedDuplexStream(header);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);
        Assert.Equal(0, buffers.Rents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameBuffersAreReturnedAndClearedForCompleteAndPartialFrames(bool partial)
    {
        var buffers = new TrackingBufferPool();
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .ReturnsAsync(new Rflush(7));
        var processor = new NinePConnectionProcessor(NullLogger.Instance, dispatcher.Object, null, buffers);
        byte[] request = Serialize(new Tflush(7, 99));
        using var stream = new ScriptedDuplexStream(partial ? request[..^1] : request);
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None);
        Assert.Equal(1, buffers.Rents);
        Assert.Equal(1, buffers.Returns);
        Assert.True(buffers.Cleared);
    }

    [Fact]
    public async Task ConnectionWorkSetTracksOnlyOutstandingResponsesAndBuildsBarriers()
    {
        var work = new NinePConnectionProcessor.ConnectionWorkSet();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Track(7, first.Task);
        Assert.Equal(1, work.PendingCount);
        Assert.Equal(1, work.ResponseCount);
        Assert.True(work.TryGetOutstanding(7, out Task? outstanding));
        Assert.Same(first.Task, outstanding);
        Assert.Same(first.Task, work.FindResponse(7));
        Assert.Null(work.FindResponse(8));
        Task barrier = work.Barrier();
        Assert.False(barrier.IsCompleted);

        first.TrySetResult();
        await barrier.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => work.PendingCount == 0 && work.ResponseCount == 0);
        Assert.False(work.TryGetOutstanding(7, out outstanding));
        Assert.Null(outstanding);

        var older = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Track(9, older.Task);
        work.Track(9, newer.Task);
        older.TrySetResult();
        await WaitForAsync(() => work.PendingCount == 1);
        Assert.Equal(1, work.ResponseCount);
        Assert.Same(newer.Task, work.FindResponse(9));
        newer.TrySetResult();
        await WaitForAsync(() => work.PendingCount == 0 && work.ResponseCount == 0);

        work.Track(8, Task.FromException(new IOException("failed")));
        await WaitForAsync(() => work.PendingCount == 0 && work.ResponseCount == 0);
        await work.Barrier().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelledPayloadReadReturnsItsRentedBuffer()
    {
        var buffers = new TrackingBufferPool();
        var processor = new NinePConnectionProcessor(NullLogger.Instance, new Mock<INinePFSDispatcher>().Object, null, buffers);
        using var stream = new ScriptedDuplexStream(Serialize(new Tflush(7, 99))) { CancelPayload = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None));
        Assert.Equal(1, buffers.Returns);
        Assert.True(buffers.Cleared);
        Assert.Throws<ArgumentNullException>(() => new NinePConnectionProcessor(NullLogger.Instance, new Mock<INinePFSDispatcher>().Object, null, null!));
    }

    [Fact]
    public async Task FrameExactlyAtMsizeIsAccepted()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), NinePDialect.NineP2000, null))
            .ReturnsAsync(new Rwrite(42, 8169));
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        using var stream = new ScriptedDuplexStream(Serialize(new Twrite(42, 1, 0, new byte[8169])));
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None);
        Assert.Equal((byte)MessageTypes.Rwrite, stream.Written.Span[4]);
        Assert.Equal((ushort)42, new Rwrite(stream.Written.Span).Tag);
    }

    [Fact]
    public async Task NonFlushRequestWithNineBytesDoesNotWaitOnAnUnrelatedTag()
    {
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(async (string id, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate) =>
            {
                if (message is NinePMessage.MsgTread read)
                {
                    readStarted.TrySetResult();
                    await releaseRead.Task;
                    return (object)new Rread(read.Item.Tag, new byte[] { 1 });
                }

                throw new InvalidOperationException("only the read should dispatch");
            });
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        byte[] malformed = { 9, 0, 0, 0, 255, 11, 0, 10, 0 };
        using var stream = new ScriptedDuplexStream(Serialize(new Tread(10, 1, 0, 1)).Concat(malformed).ToArray());
        Task processing = processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Contains(MessageTypes.Rerror, ResponseTypes(stream.Written.Span));
        }
        finally
        {
            releaseRead.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task AFlushIsAnsweredOnlyAfterTheRequestItFlushes()
    {
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(async (string id, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate) =>
            {
                if (message is NinePMessage.MsgTread read)
                {
                    await releaseRead.Task;
                    return (object)new Rread(read.Item.Tag, new byte[] { 1 });
                }

                flushDispatched.TrySetResult();
                return new Rflush(12);
            });
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        byte[] input = Serialize(new Tread(10, 1, 0, 1)).Concat(Serialize(new Tflush(12, 10))).ToArray();
        using var stream = new ScriptedDuplexStream(input);
        Task processing = processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None);
        await flushDispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            // Time enough for an Rflush that did not wait for the read to reach the wire.
            await Task.Delay(200);
            Assert.Empty(stream.Written.ToArray());
        }
        finally
        {
            releaseRead.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(new[] { MessageTypes.Rread, MessageTypes.Rflush }, ResponseTypes(stream.Written.Span));
    }

    [Fact]
    public async Task VersionIsAWireBarrierAndNextRequestUsesNewDialect()
    {
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var versionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(async (string id, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate) =>
            {
                if (message is NinePMessage.MsgTread read)
                {
                    await releaseRead.Task;
                    return (object)new Rread(read.Item.Tag, new byte[] { 1 });
                }

                if (message is NinePMessage.MsgTversion)
                {
                    versionReached.TrySetResult();
                    return new Rversion(65535, 512, "9P2000.L");
                }

                Assert.Equal(NinePDialect.NineP2000L, dialect);
                return new Rflush(12);
            });
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        byte[] input = Serialize(new Tread(10, 1, 0, 1))
            .Concat(Serialize(new Tversion(65535, 512, "9P2000.L")))
            .Concat(Serialize(new Tflush(12, 10))).ToArray();
        using var stream = new ScriptedDuplexStream(input);
        var session = new NinePConnectionProcessor.ClientSession();
        Task processing = processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);
        await versionReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Empty(stream.Written.ToArray());
        }
        finally
        {
            releaseRead.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(new[] { MessageTypes.Rread, MessageTypes.Rversion, MessageTypes.Rflush }, ResponseTypes(stream.Written.Span));
        Assert.Equal(512U, session.MSize);
        Assert.Equal(NinePDialect.NineP2000L, session.Dialect);
    }

    [Fact]
    public async Task VersionNegotiationCompletesBeforeTheProcessorReadsTheNextRequest()
    {
        var versionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVersion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(async (string id, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate) =>
            {
                if (message is NinePMessage.MsgTversion)
                {
                    versionStarted.TrySetResult();
                    await releaseVersion.Task;
                    return (object)new Rversion(NinePConstants.NoTag, 512, "9P2000");
                }

                nextDispatched.TrySetResult();
                return new Rflush(12);
            });
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        byte[] input = Serialize(new Tversion(NinePConstants.NoTag, 512, "9P2000"))
            .Concat(Serialize(new Tflush(12, 99))).ToArray();
        using var stream = new ScriptedDuplexStream(input);
        Task processing = processor.ProcessStreamAsync(
            stream,
            null,
            new NinePConnectionProcessor.ClientSession(),
            CancellationToken.None);
        await versionStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        try
        {
            Task first = await Task.WhenAny(nextDispatched.Task, Task.Delay(100));
            Assert.NotSame(nextDispatched.Task, first);
        }
        finally
        {
            releaseVersion.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.True(nextDispatched.Task.IsCompletedSuccessfully);
        Assert.Equal(new[] { MessageTypes.Rversion, MessageTypes.Rflush }, ResponseTypes(stream.Written.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleClientProcessesRequestsAndClosesTheSocket(bool authenticationFails)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var accepted = await listener.AcceptTcpClientAsync();
        var security = new Mock<INinePTransportSecurity>();
        security.Setup(value => value.AuthenticateAsync(It.IsAny<Stream>(), It.IsAny<EndpointConfig>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, EndpointConfig endpoint, CancellationToken token) => authenticationFails
                ? Task.FromException<TransportSecurityResult>(new IOException("authentication failed"))
                : Task.FromResult(new TransportSecurityResult(stream, null)));
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .ReturnsAsync(new Rflush(7));
        var processor = CreateProcessor(dispatcher, security.Object);
        Task processing = processor.HandleClientAsync(accepted, new EndpointConfig(), CancellationToken.None);
        if (!authenticationFails)
        {
            await peer.GetStream().WriteAsync(Serialize(new Tflush(7, 99)));
            byte[] response = new byte[7];
            await peer.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((byte)MessageTypes.Rflush, response[4]);
            peer.Client.Shutdown(SocketShutdown.Send);
        }

        await processing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(accepted.Client);
        security.VerifyAll();
    }

    [Fact]
    public async Task MalformedLinuxRequestReturnsNumericErrorWithoutCallingDispatcher()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession { Dialect = NinePDialect.NineP2000L };
        byte[] malformed = { 7, 0, 0, 0, 255, 99, 0 };
        using var stream = new ScriptedDuplexStream(malformed);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);
        Assert.Equal((byte)MessageTypes.Rlerror, stream.Written.Span[4]);
        var error = new Rlerror(stream.Written.Span);
        Assert.Equal((ushort)99, error.Tag);
        Assert.Equal((uint)LinuxErrorCode.EINVAL, error.Ecode);
        dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticateTransportAsync_AuthorizedCertificate_StoresSessionCertificate()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var security = new StubTransportSecurity();
        using var stream = new MemoryStream();
        using var certificate = CreateSelfSignedCertificate();

        security.Result = new TransportSecurityResult(stream, certificate);

        var processor = CreateProcessor(dispatcher, security);
        var session = new NinePConnectionProcessor.ClientSession();
        var endpoint = new EndpointConfig { Address = "127.0.0.1", Port = 564, Protocol = "tls" };

        var securedStream = await processor.AuthenticateTransportAsync(stream, endpoint, session, null, CancellationToken.None);

        Assert.Same(stream, securedStream);
        Assert.Same(certificate, session.ClientCertificate);
    }

    [Fact]
    public async Task DefaultNinePTransportSecurity_NonTls_ReturnsOriginalStream()
    {
        var transportSecurity = new DefaultNinePTransportSecurity();
        using var stream = new MemoryStream();

        var result = await transportSecurity.AuthenticateAsync(
            stream,
            new EndpointConfig { Address = "127.0.0.1", Port = 564, Protocol = "tcp" },
            CancellationToken.None);

        Assert.Same(stream, result.Stream);
        Assert.Null(result.ClientCertificate);
    }

    [Fact]
    public async Task DefaultNinePTransportSecurity_TlsWithoutServerCertificate_Throws()
    {
        var transportSecurity = new DefaultNinePTransportSecurity();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transportSecurity.AuthenticateAsync(
                new MemoryStream(),
                new EndpointConfig { Address = "127.0.0.1", Port = 564, Protocol = "tls" },
                CancellationToken.None));

        Assert.Contains("server certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessStreamAsync_DispatcherException_WritesRerror()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();
        var request = Serialize(new Tflush(7, 99));

        dispatcher
            .Setup(d => d.DispatchAsync(session.SessionId, It.IsAny<NinePMessage>(), NinePDialect.NineP2000, null))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        using var stream = new ScriptedDuplexStream(request);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.NotEmpty(written);
        Assert.Equal((byte)MessageTypes.Rerror, written[4]);
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(5, 2)));
    }

    [Fact]
    public async Task ProcessStreamAsync_EOF_ExitsCleanly()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();

        using var stream = new ScriptedDuplexStream(Array.Empty<byte>());
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.Empty(written);
    }

    [Fact]
    public async Task ProcessStreamAsync_PartialHeader_ExitsWithoutResponse()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();

        var partialHeader = new byte[] { 0x13, 0x00, 0x00 };
        using var stream = new ScriptedDuplexStream(partialHeader);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.Empty(written);
    }

    [Fact]
    public async Task ProcessStreamAsync_InvalidSize_ExitsWithoutResponse()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();

        var invalidFrame = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(invalidFrame.AsSpan(0, 4), 3);
        invalidFrame[4] = (byte)MessageTypes.Tflush;

        using var stream = new ScriptedDuplexStream(invalidFrame);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.Empty(written);
    }

    [Fact]
    public async Task ProcessStreamAsync_OversizedFrame_ExitsWithoutResponse()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();

        var oversizedFrame = new byte[NinePConstants.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedFrame.AsSpan(0, 4), session.MSize + 1);
        oversizedFrame[4] = (byte)MessageTypes.Tflush;

        using var stream = new ScriptedDuplexStream(oversizedFrame);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.Empty(written);
    }

    [Fact]
    public async Task ProcessStreamAsync_PartialPayload_ExitsWithoutResponse()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher, new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();

        var partialFrame = new byte[NinePConstants.HeaderSize + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(partialFrame.AsSpan(0, 4), 20);
        partialFrame[4] = (byte)MessageTypes.Tflush;

        using var stream = new ScriptedDuplexStream(partialFrame);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        var written = stream.Written.ToArray();
        Assert.Empty(written);
    }

    [Fact]
    public async Task ProcessStreamAsync_ReadsFlushWhileEarlierRequestIsOutstanding()
    {
        var dispatcher = new MultiplexingDispatcher();
        var processor = new NinePConnectionProcessor(
            NullLogger.Instance,
            dispatcher,
            new StubTransportSecurity());
        var session = new NinePConnectionProcessor.ClientSession();
        byte[] read = Serialize(new Tread(10, 1, 0, 1));
        byte[] flush = Serialize(new Tflush(11, 10));
        byte[] input = new byte[read.Length + flush.Length];
        read.CopyTo(input, 0);
        flush.CopyTo(input, read.Length);

        using var stream = new ScriptedDuplexStream(input);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            new[] { MessageTypes.Rread, MessageTypes.Rflush },
            ResponseTypes(stream.Written.Span));
        Assert.True(dispatcher.SessionClosed);
    }

    private static NinePConnectionProcessor CreateProcessor(
        Mock<INinePFSDispatcher> dispatcher,
        INinePTransportSecurity security)
    {
        return new NinePConnectionProcessor(NullLogger.Instance, dispatcher.Object, security);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ninepsharp-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static byte[] Serialize(ISerializable message)
    {
        byte[] buffer = new byte[message.Size];
        message.WriteTo(buffer);
        return buffer;
    }

    private static IReadOnlyList<MessageTypes> ResponseTypes(ReadOnlySpan<byte> responses)
    {
        var result = new List<MessageTypes>();
        int offset = 0;
        while (offset < responses.Length)
        {
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(responses.Slice(offset, 4)));
            result.Add((MessageTypes)responses[offset + 4]);
            offset += size;
        }

        return result;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class StubTransportSecurity : INinePTransportSecurity
    {
        public TransportSecurityResult Result { get; set; }

        public Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class MultiplexingDispatcher : INinePFSDispatcher, INinePSessionLifecycle
    {
        private readonly TaskCompletionSource readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool SessionClosed { get; private set; }

        public async Task<object> DispatchAsync(
            string sessionId,
            NinePMessage message,
            NinePDialect dialect,
            X509Certificate2? certificate = null)
        {
            if (message is NinePMessage.MsgTread read)
            {
                readStarted.TrySetResult();
                await releaseRead.Task;
                return new Rread(read.Item.Tag, new byte[] { 1 });
            }

            if (message is NinePMessage.MsgTflush flush)
            {
                await readStarted.Task;
                releaseRead.TrySetResult();
                return new Rflush(flush.Item.Tag);
            }

            throw new NotSupportedException();
        }

        public Task CloseSessionAsync(string sessionId)
        {
            SessionClosed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly byte[] input;
        private readonly MemoryStream written = new();
        private int position;

        public ScriptedDuplexStream(byte[] input)
        {
            this.input = input;
        }

        public ReadOnlyMemory<byte> Written => written.ToArray();

        public bool CancelPayload { get; init; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => input.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Max(0, input.Length - position);
            int toCopy = Math.Min(count, available);
            if (toCopy == 0)
            {
                return 0;
            }

            Buffer.BlockCopy(input, position, buffer, offset, toCopy);
            position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (CancelPayload && position >= NinePConstants.HeaderSize)
            {
                throw new OperationCanceledException();
            }

            int available = Math.Max(0, input.Length - position);
            int toCopy = Math.Min(buffer.Length, available);
            if (toCopy == 0)
            {
                return ValueTask.FromResult(0);
            }

            input.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            written.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // A client like 9front's, which reuses a tag the moment its reply arrives: the second request can be
    // read only once the first reply is written, and that write returns only after the client is done.
    private sealed class TagReusingStream(byte[] first, byte[] second) : Stream
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
        private readonly MemoryStream written = new();
        private readonly TaskCompletionSource replied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[] current = first;
        private int position;

        public ReadOnlyMemory<byte> Written
        {
            get
            {
                lock (written)
                {
                    return written.ToArray();
                }
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position == current.Length)
            {
                if (current != first)
                {
                    finished.TrySetResult();
                    return 0;
                }

                await replied.Task.WaitAsync(Wait, cancellationToken);
                current = second;
                position = 0;
            }

            int count = Math.Min(buffer.Length, current.Length - position);
            current.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (written)
            {
                written.Write(buffer.Span);
            }

            replied.TrySetResult();
            await finished.Task.WaitAsync(Wait, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TrackingBufferPool : ArrayPool<byte>
    {
        internal int Rents { get; private set; }

        internal int Returns { get; private set; }

        internal bool Cleared { get; private set; }

        internal List<bool> ClearFlags { get; } = new();

        public override byte[] Rent(int minimumLength)
        {
            Rents++;
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Returns++;
            Cleared = clearArray;
            ClearFlags.Add(clearArray);
        }
    }
}

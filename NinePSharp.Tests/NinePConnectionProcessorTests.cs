using System.Buffers.Binary;
using System.Net;
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

    private sealed class StubTransportSecurity : INinePTransportSecurity
    {
        public TransportSecurityResult Result { get; set; }

        public Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class ScriptedDuplexStream : Stream
    {
        private readonly byte[] _input;
        private readonly MemoryStream _written = new();
        private int _position;

        public ScriptedDuplexStream(byte[] input)
        {
            _input = input;
        }

        public ReadOnlyMemory<byte> Written => _written.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _input.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Max(0, _input.Length - _position);
            int toCopy = Math.Min(count, available);
            if (toCopy == 0)
            {
                return 0;
            }

            Buffer.BlockCopy(_input, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int available = Math.Max(0, _input.Length - _position);
            int toCopy = Math.Min(buffer.Length, available);
            if (toCopy == 0)
            {
                return ValueTask.FromResult(0);
            }

            _input.AsMemory(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

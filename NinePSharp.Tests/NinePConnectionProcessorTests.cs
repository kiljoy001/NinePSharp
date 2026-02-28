using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Interfaces;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class NinePConnectionProcessorTests
{
    [Property(MaxTest = 64)]
    public bool ProcessStreamAsync_InvalidFrameSizes_DoNotReachDispatcher(int rawSize)
    {
        uint size = unchecked((uint)rawSize);
        if (size >= NinePConstants.HeaderSize && size <= 8192)
        {
            return true;
        }

        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher);
        var session = new NinePConnectionProcessor.ClientSession();

        byte[] header = new byte[NinePConstants.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), size);
        header[4] = (byte)MessageTypes.Tflush;

        using var stream = new ScriptedDuplexStream(header);
        processor.ProcessStreamAsync(stream, new IPEndPoint(IPAddress.Loopback, 564), session, CancellationToken.None).Sync();

        dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string?>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2?>()),
            Times.Never);

        return stream.Written.Length == 0;
    }

    [Property(MaxTest = 32)]
    public bool ProcessStreamAsync_PartialHeaders_DoNotReachDispatcher(PositiveInt countSeed)
    {
        int count = (countSeed.Get % (NinePConstants.HeaderSize - 1)) + 1;
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher);
        var session = new NinePConnectionProcessor.ClientSession();

        byte[] partialHeader = new byte[count];
        for (int i = 0; i < partialHeader.Length; i++)
        {
            partialHeader[i] = (byte)(i + 1);
        }

        using var stream = new ScriptedDuplexStream(partialHeader);
        processor.ProcessStreamAsync(stream, null, session, CancellationToken.None).Sync();

        dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string?>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2?>()),
            Times.Never);

        return stream.Written.Length == 0;
    }

    [Fact]
    public async Task ProcessStreamAsync_DispatcherException_Writes_Rerror_Frame()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher);
        var session = new NinePConnectionProcessor.ClientSession();
        var request = Serialize(new Tflush(7, 99));

        dispatcher
            .Setup(d => d.DispatchAsync(session.SessionId, It.IsAny<NinePMessage>(), NinePDialect.NineP2000, null))
            .ThrowsAsync(new InvalidOperationException("kaboom"));

        using var stream = new ScriptedDuplexStream(request);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        byte[] written = stream.Written.ToArray();
        Assert.NotEmpty(written);
        Assert.Equal((byte)MessageTypes.Rerror, written[4]);
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(5, 2)));
    }

    [Fact]
    public async Task ProcessStreamAsync_Rversion_Updates_Session_Before_Next_Dispatch()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher);
        var session = new NinePConnectionProcessor.ClientSession();

        byte[] version = Serialize(new Tversion(1, 4096, "9P2000"));
        byte[] flush = Serialize(new Tflush(2, 1));
        byte[] input = new byte[version.Length + flush.Length];
        Buffer.BlockCopy(version, 0, input, 0, version.Length);
        Buffer.BlockCopy(flush, 0, input, version.Length, flush.Length);

        dispatcher
            .Setup(d => d.DispatchAsync(session.SessionId, It.Is<NinePMessage>(m => m.IsMsgTversion), NinePDialect.NineP2000, null))
            .ReturnsAsync(new Rversion(1, 16384, "9P2000.u"));
        dispatcher
            .Setup(d => d.DispatchAsync(session.SessionId, It.Is<NinePMessage>(m => m.IsMsgTflush), NinePDialect.NineP2000U, null))
            .ReturnsAsync(new Rflush(2));

        using var stream = new ScriptedDuplexStream(input);
        await processor.ProcessStreamAsync(stream, null, session, CancellationToken.None);

        Assert.Equal(16384u, session.MSize);
        Assert.Equal(NinePDialect.NineP2000U, session.Dialect);
        dispatcher.VerifyAll();
    }

    [Fact]
    public async Task AuthenticateTransportAsync_TlsCertificate_IsAuthorized_And_Stored()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var auth = new Mock<IEmercoinAuthService>(MockBehavior.Strict);
        var security = new StubTransportSecurity();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ninepsharp-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var stream = new MemoryStream();

        security.Result = new TransportSecurityResult(stream, certificate);
        auth.Setup(a => a.IsCertificateAuthorizedAsync(certificate)).ReturnsAsync(true);

        var processor = CreateProcessor(dispatcher, auth, security);
        var session = new NinePConnectionProcessor.ClientSession();
        var endpoint = new EndpointConfig { Address = "127.0.0.1", Port = 564, Protocol = "tls" };

        Stream secured = await processor.AuthenticateTransportAsync(stream, endpoint, session, null, CancellationToken.None);

        Assert.Same(stream, secured);
        Assert.Same(certificate, session.ClientCertificate);
        auth.Verify(a => a.IsCertificateAuthorizedAsync(certificate), Times.Once);
        Assert.Equal(1, security.CallCount);
    }

    [Fact]
    public async Task AuthenticateTransportAsync_TlsWithoutCertificate_SkipsAuthorization()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var auth = new Mock<IEmercoinAuthService>(MockBehavior.Strict);
        var security = new StubTransportSecurity();
        using var stream = new MemoryStream();

        security.Result = new TransportSecurityResult(stream, null);

        var processor = CreateProcessor(dispatcher, auth, security);
        var session = new NinePConnectionProcessor.ClientSession();
        var endpoint = new EndpointConfig { Address = "127.0.0.1", Port = 564, Protocol = "tls" };

        Stream secured = await processor.AuthenticateTransportAsync(stream, endpoint, session, null, CancellationToken.None);

        Assert.Same(stream, secured);
        Assert.Null(session.ClientCertificate);
        auth.Verify(a => a.IsCertificateAuthorizedAsync(It.IsAny<X509Certificate2>()), Times.Never);
        Assert.Equal(1, security.CallCount);
    }

    private static NinePConnectionProcessor CreateProcessor(Mock<INinePFSDispatcher> dispatcher)
    {
        var auth = new Mock<IEmercoinAuthService>(MockBehavior.Strict);
        return CreateProcessor(dispatcher, auth, new StubTransportSecurity());
    }

    private static NinePConnectionProcessor CreateProcessor(
        Mock<INinePFSDispatcher> dispatcher,
        Mock<IEmercoinAuthService> auth,
        INinePTransportSecurity security)
    {
        return new NinePConnectionProcessor(NullLogger<NinePServer>.Instance, dispatcher.Object, auth.Object, security);
    }

    private static byte[] Serialize(ISerializable message)
    {
        byte[] buffer = new byte[message.Size];
        message.WriteTo(buffer);
        return buffer;
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

    private sealed class StubTransportSecurity : INinePTransportSecurity
    {
        public int CallCount { get; private set; }
        public TransportSecurityResult Result { get; set; }

        public Task<TransportSecurityResult> AuthenticateAsync(Stream transport, EndpointConfig endpoint, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(Result.Equals(default(TransportSecurityResult))
                ? new TransportSecurityResult(transport, null)
                : Result);
        }
    }
}

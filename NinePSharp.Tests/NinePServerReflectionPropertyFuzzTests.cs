using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class NinePServerReflectionPropertyFuzzTests
{
    [Property(MaxTest = 70)]
    public bool DispatchMessageAsync_Fuzz_Malformed_Buffers_Returns_Rerror(NonNegativeInt seed)
    {
        var random = new Random(seed.Get + 1337);
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        var processor = CreateProcessor(dispatcher);

        var session = new NinePConnectionProcessor.ClientSession();

        for (int i = 0; i < 40; i++)
        {
            var len = random.Next(0, 40);
            var data = new byte[len];
            random.NextBytes(data);

            ushort tag = (ushort)(i + 1);
            object result;
            try
            {
                result = processor.DispatchMessageAsync(new ReadOnlyMemory<byte>(data), MessageTypes.Tread, tag, session).Sync();
            }
            catch
            {
                return false;
            }

            if (result is not Rerror err || err.Tag != tag)
            {
                return false;
            }
        }

        dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string?>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), It.IsAny<X509Certificate2?>()),
            Times.Never);

        return true;
    }

    [Fact]
    public async Task DispatchMessageAsync_Valid_Message_Forwards_To_Dispatcher_With_Session_Dotu()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<string?>(), It.IsAny<NinePMessage>(), NinePDialect.NineP2000U, null))
            .ReturnsAsync(new Rflush(77));

        var processor = CreateProcessor(dispatcher);
        var session = new NinePConnectionProcessor.ClientSession { Dialect = NinePDialect.NineP2000U };

        var message = new Tflush(5, 1);
        var buffer = Serialize(message);

        var response = await processor.DispatchMessageAsync(new ReadOnlyMemory<byte>(buffer), MessageTypes.Tflush, (ushort)5, session);

        Assert.IsType<Rflush>(response);
        dispatcher.Verify(d => d.DispatchAsync(session.SessionId, It.IsAny<NinePMessage>(), NinePDialect.NineP2000U, null), Times.Once);
    }

    [Property(MaxTest = 45)]
    public bool SendResponseAsync_Serializable_Writes_Exact_Bytes(NonEmptyString text, PositiveInt seed)
    {
        var processor = CreateProcessor(new Mock<INinePFSDispatcher>(MockBehavior.Strict));

        var safe = text.Get.Length > 32 ? text.Get[..32] : text.Get;
        var response = new Rerror((ushort)(seed.Get % ushort.MaxValue), safe);

        using var stream = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        processor.SendResponseAsync(stream, response, writeLock, CancellationToken.None).Sync();

        var expected = Serialize(response);
        var actual = stream.ToArray();

        if (actual.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                return false;
            }
        }

        uint frameSize = BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(0, 4));
        return frameSize == response.Size;
    }

    [Fact]
    public async Task SendResponseAsync_NonSerializable_Response_Writes_Nothing()
    {
        var processor = CreateProcessor(new Mock<INinePFSDispatcher>(MockBehavior.Strict));

        using var stream = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        await processor.SendResponseAsync(stream, new object(), writeLock, CancellationToken.None);

        Assert.Equal(0, stream.Length);
    }

    private static NinePConnectionProcessor CreateProcessor(Mock<INinePFSDispatcher> dispatcher)
    {
        var auth = new Mock<IEmercoinAuthService>(MockBehavior.Strict);
        return new NinePConnectionProcessor(
            NullLogger<NinePServer>.Instance,
            dispatcher.Object,
            auth.Object);
    }

    private static byte[] Serialize(ISerializable message)
    {
        byte[] buffer = new byte[message.Size];
        message.WriteTo(buffer);
        return buffer;
    }
}

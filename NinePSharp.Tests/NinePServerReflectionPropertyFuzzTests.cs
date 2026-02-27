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
using Microsoft.Extensions.Options;
using Moq;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
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
        var sut = CreateServer(dispatcher);

        var session = CreateClientSession();
        var method = GetDispatchMessageAsyncMethod();

        for (int i = 0; i < 40; i++)
        {
            var len = random.Next(0, 40);
            var data = new byte[len];
            random.NextBytes(data);

            ushort tag = (ushort)(i + 1);
            object[] args =
            {
                new ReadOnlyMemory<byte>(data),
                MessageTypes.Tread,
                tag,
                session
            };

            object result;
            try
            {
                result = ((Task<object>)method.Invoke(sut, args)!).Sync();
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
            d => d.DispatchAsync(It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), It.IsAny<X509Certificate2?>()),
            Times.Never);

        return true;
    }

    [Fact]
    public async Task DispatchMessageAsync_Valid_Message_Forwards_To_Dispatcher_With_Session_Dotu()
    {
        var dispatcher = new Mock<INinePFSDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<NinePMessage>(), NinePDialect.NineP2000U, null))
            .ReturnsAsync(new Rflush(77));

        var sut = CreateServer(dispatcher);
        var session = CreateClientSession();
        SetSessionDotU(session, NinePDialect.NineP2000U);

        var method = GetDispatchMessageAsyncMethod();
        var message = new Tflush(5, 1);
        var buffer = Serialize(message);

        var response = await (Task<object>)method.Invoke(sut, new object[]
        {
            new ReadOnlyMemory<byte>(buffer),
            MessageTypes.Tflush,
            (ushort)5,
            session
        })!;

        Assert.IsType<Rflush>(response);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<NinePMessage>(), NinePDialect.NineP2000U, null), Times.Once);
    }

    [Property(MaxTest = 45)]
    public bool SendResponseAsync_Serializable_Writes_Exact_Bytes(NonEmptyString text, PositiveInt seed)
    {
        var sut = CreateServer(new Mock<INinePFSDispatcher>(MockBehavior.Strict));
        var method = GetSendResponseAsyncMethod();

        var safe = text.Get.Length > 32 ? text.Get[..32] : text.Get;
        var response = new Rerror((ushort)(seed.Get % ushort.MaxValue), safe);

        using var stream = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        ((Task)method.Invoke(sut, new object[] { stream, response, writeLock })!).Sync();

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
        var sut = CreateServer(new Mock<INinePFSDispatcher>(MockBehavior.Strict));
        var method = GetSendResponseAsyncMethod();

        using var stream = new MemoryStream();
        using var writeLock = new SemaphoreSlim(1, 1);

        await (Task)method.Invoke(sut, new object[] { stream, new object(), writeLock })!;

        Assert.Equal(0, stream.Length);
    }

    private static NinePServer CreateServer(Mock<INinePFSDispatcher> dispatcher)
    {
        var cluster = new Mock<IClusterManager>(MockBehavior.Strict);
        var auth = new Mock<IEmercoinAuthService>(MockBehavior.Strict);

        var config = new ServerConfig
        {
            Endpoints =
            {
                new EndpointConfig { Address = "127.0.0.1", Port = 0, Protocol = "tcp" }
            }
        };

        return new NinePServer(
            NullLogger<NinePServer>.Instance,
            Options.Create(config),
            Array.Empty<IProtocolBackend>(),
            dispatcher.Object,
            cluster.Object,
            new ConfigurationBuilder().Build(),
            auth.Object);
    }

    private static byte[] Serialize(ISerializable message)
    {
        byte[] buffer = new byte[message.Size];
        message.WriteTo(buffer);
        return buffer;
    }

    private static object CreateClientSession()
    {
        var type = typeof(NinePServer).GetNestedType("ClientSession", System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ClientSession nested type not found.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Failed to create ClientSession instance.");
    }

    private static void SetSessionDotU(object session, NinePDialect dotu)
    {
        var type = session.GetType();
        var property = type.GetProperty("Dialect")
            ?? throw new InvalidOperationException("ClientSession.Dialect property not found.");
        property.SetValue(session, dotu);
    }

    private static System.Reflection.MethodInfo GetDispatchMessageAsyncMethod()
        => typeof(NinePServer).GetMethod("DispatchMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
           ?? throw new InvalidOperationException("NinePServer.DispatchMessageAsync not found.");

    private static System.Reflection.MethodInfo GetSendResponseAsyncMethod()
        => typeof(NinePServer).GetMethod("SendResponseAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
           ?? throw new InvalidOperationException("NinePServer.SendResponseAsync not found.");
}

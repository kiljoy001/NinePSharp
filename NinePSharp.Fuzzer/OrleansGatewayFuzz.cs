using System.Buffers.Binary;
using System.Security.Cryptography.X509Certificates;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Orleans;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Parser;
using Orleans;

namespace NinePSharp.Fuzzer;

/// <summary>Stateful request-sequence fuzzing through the gateway and Orleans adapters, with deterministic grain doubles.</summary>
internal static class OrleansGatewayFuzz
{
    private static readonly ResourceHandle Root = new(new ResourceIdentity("fuzz", "device", 1), QidType.QTDIR);
    private static readonly ResourceHandle File = new(new ResourceIdentity("fuzz", "device", 2), QidType.QTFILE);

    internal static void Run(Stream input)
    {
        byte[] data = new byte[512];
        int length = input.ReadAtLeast(data, 1, throwOnEndOfStream: false);
        if (length == 0)
        {
            return;
        }

        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var group = new Mock<IVProcessGroupGrain>(MockBehavior.Strict);
        group.Setup(value => value.GetSnapshotAsync()).ReturnsAsync(new NamespaceSnapshot(0, Array.Empty<MountHead>()).ToModel());
        factory.Setup(value => value.GetGrain<IVProcessGroupGrain>("group", null)).Returns(group.Object);
        factory.Setup(value => value.GetGrain<IMountableResourceGrain>("device", null)).Returns(CreateResource().Object);
        var resolver = new RegisteredMountableResourceResolver(
            factory.Object,
            new[] { ResourceProviderRegistration.For<IMountableResourceGrain>("fuzz") });
        var dispatcher = new DistributedNamespaceDispatcher(
            new DistributedNamespaceOperations(factory.Object, new OrleansResourceOperations(resolver)), new AttachResolver());
        uint msize = 256U + data[0];
        try
        {
            Dispatch(new Tversion(65535, msize, "9P2000"));
            Dispatch(new Tattach(1, 0, NinePConstants.NoFid, "fuzz", "/"));
            Dispatch(new Twalk(2, 0, 1, new[] { "file" }));
            Dispatch(new Topen(3, 1, NinePConstants.ORDWR));
            for (int index = 1; index < length; index++)
            {
                byte value = data[index];
                ushort tag = (ushort)(index + 4);
                uint fid = (uint)(value % 4);
                ISerializable request = (value % 12) switch
                {
                    0 => new Tattach(tag, fid, NinePConstants.NoFid, "fuzz", "/"),
                    1 => new Twalk(tag, fid, (fid + 1) % 4, new[] { "file" }),
                    2 => new Twalk(tag, fid, (fid + 1) % 4, new[] { "missing" }),
                    3 => new Topen(tag, fid, (byte)(value % 3)),
                    4 => new Tread(tag, fid, value, value % 2 == 0 ? uint.MaxValue : value),
                    5 => new Twrite(tag, fid, value, data.AsMemory(0, Math.Min(length, 32))),
                    6 => new Tstat(tag, fid),
                    7 => new Tclunk(tag, fid),
                    8 => new Tremove(tag, fid),
                    9 => new Tflush(tag, (ushort)value),
                    10 => new Tcreate(tag, fid, "created", NinePConstants.Mode0644, NinePConstants.ORDWR),
                    _ => new Tversion(65535, msize, "9P2000"),
                };
                Dispatch(request);
            }
        }
        finally
        {
            dispatcher.CloseSessionAsync("fuzz").GetAwaiter().GetResult();
        }

        void Dispatch(ISerializable request)
        {
            byte[] wire = new byte[request.Size];
            request.WriteTo(wire);
            var parsed = NinePParser.parse(NinePDialect.NineP2000, wire.AsMemory());
            if (parsed.IsError)
            {
                throw new InvalidOperationException("Generated request did not parse.");
            }

            object result = dispatcher.DispatchAsync("fuzz", parsed.ResultValue, NinePDialect.NineP2000).GetAwaiter().GetResult();
            if (result is not ISerializable response || response.Size > msize)
            {
                throw new InvalidOperationException("Reply exceeded negotiated msize.");
            }

            byte[] reply = new byte[response.Size];
            response.WriteTo(reply);
            if (BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(5)) != BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(5)))
            {
                throw new InvalidOperationException("Reply tag did not match request.");
            }
        }
    }

    private static Mock<IMountableResourceGrain> CreateResource()
    {
        var resource = new Mock<IMountableResourceGrain>(MockBehavior.Strict);
        resource.Setup(value => value.WalkAsync(It.IsAny<ResourceHandleModel>(), It.IsAny<string>()))
            .Returns((ResourceHandleModel directory, string name) => Task.FromResult(name == "file" ? File.ToModel() : null));
        resource.Setup(value => value.ReadDirectoryAsync(It.IsAny<ResourceHandleModel>()))
            .ReturnsAsync(new[] { new ResourceDirectoryEntryModel("file", File.ToModel()) });
        resource.Setup(value => value.OpenAsync(It.IsAny<ResourceHandleModel>(), It.IsAny<byte>(), It.IsAny<ResourceOperationContextModel>()))
            .Returns((ResourceHandleModel handle, byte mode, ResourceOperationContextModel context) =>
                Task.FromResult(new ResourceOpenHandleModel(handle, "open", mode, 0)));
        resource.Setup(value => value.ReadAsync(It.IsAny<ResourceOpenHandleModel>(), It.IsAny<ulong>(), It.IsAny<uint>()))
            .Returns((ResourceOpenHandleModel handle, ulong offset, uint count) => Task.FromResult(new byte[count]));
        resource.Setup(value => value.WriteAsync(It.IsAny<ResourceOpenHandleModel>(), It.IsAny<ulong>(), It.IsAny<byte[]>(), It.IsAny<ResourceOperationContextModel>()))
            .Returns((ResourceOpenHandleModel handle, ulong offset, byte[] data, ResourceOperationContextModel context) => Task.FromResult((uint)data.Length));
        resource.Setup(value => value.StatAsync(It.IsAny<ResourceHandleModel>()))
            .Returns((ResourceHandleModel handle) => Task.FromResult(new ResourceStatModel(handle, "file", NinePConstants.Mode0644, 0, 0, 0, "fuzz", "fuzz", "fuzz")));
        resource.Setup(value => value.CreateAndOpenAsync(It.IsAny<ResourceHandleModel>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<byte>(), It.IsAny<ResourceOperationContextModel>()))
            .ReturnsAsync(new ResourceOpenHandleModel(File.ToModel(), "created", NinePConstants.ORDWR, 0));
        resource.Setup(value => value.ClunkAsync(It.IsAny<ResourceOpenHandleModel>(), It.IsAny<ResourceOperationContextModel>())).Returns(Task.CompletedTask);
        resource.Setup(value => value.RemoveAsync(It.IsAny<ResourceHandleModel>(), It.IsAny<ResourceOpenHandleModel?>(), It.IsAny<ResourceOperationContextModel>())).Returns(Task.CompletedTask);
        return resource;
    }

    private sealed class AttachResolver : IDistributedNamespaceAttachResolver
    {
        public ValueTask<DistributedNamespaceAttach> ResolveAsync(
            string sessionId, Tattach request, NinePDialect dialect, X509Certificate2? certificate, CancellationToken cancellationToken)
            => ValueTask.FromResult(new DistributedNamespaceAttach("group", 1, "fuzz", Root));
    }
}

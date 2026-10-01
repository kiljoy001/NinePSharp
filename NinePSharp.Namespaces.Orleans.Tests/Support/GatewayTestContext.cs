using System.Security.Cryptography.X509Certificates;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Parser;
using Orleans;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

internal sealed class GatewayTestContext
{
    internal static readonly ResourceHandle Root = new(new ResourceIdentity("test", "resource", 1), QidType.QTDIR);
    internal static readonly ResourceHandle File = new(new ResourceIdentity("test", "resource", 2), QidType.QTFILE);
    internal Mock<IGrainFactory> Factory { get; } = new();
    internal Mock<IResourceDataOperations> Resources { get; } = new();
    internal TestAttachResolver Attach { get; } = new();
    internal DistributedNamespaceDispatcher Dispatcher { get; }
    internal List<ResourceOperationContext> Mutations { get; } = new();

    internal GatewayTestContext(uint maximumMessageSize = 1024 * 1024)
    {
        var group = new Mock<IVProcessGroupGrain>();
        group.Setup(value => value.GetSnapshotAsync()).ReturnsAsync(new NamespaceSnapshot(0, Array.Empty<MountHead>()).ToModel());
        Factory.Setup(value => value.GetGrain<IVProcessGroupGrain>(It.IsAny<string>(), null)).Returns(group.Object);
        Resources.Setup(value => value.WalkAsync(It.IsAny<ResourceHandle>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceHandle directory, string name, CancellationToken token) =>
                ValueTask.FromResult<ResourceHandle?>(name == "file" ? File : null));
        Resources.Setup(value => value.OpenAsync(It.IsAny<ResourceHandle>(), It.IsAny<byte>(), It.IsAny<ResourceOperationContext>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceHandle handle, byte mode, ResourceOperationContext context, CancellationToken token) =>
            {
                Mutations.Add(context);
                return ValueTask.FromResult(new ResourceOpenHandle(handle, context.OperationId.ToString(), mode, 0));
            });
        Resources.Setup(value => value.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceOpenHandle handle, ulong offset, uint count, CancellationToken token) =>
                ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[count]));
        Resources.Setup(value => value.ReadDirectoryAsync(It.IsAny<ResourceHandle>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ResourceDirectoryEntry("file", File) });
        Resources.Setup(value => value.StatAsync(It.IsAny<ResourceHandle>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceHandle handle, CancellationToken token) => ValueTask.FromResult(new ResourceStat(
                handle, "file", 0x1A4, 0, 0, 0, "user", "user", "user")));
        Dispatcher = new DistributedNamespaceDispatcher(new DistributedNamespaceOperations(Factory.Object, Resources.Object), Attach, maximumMessageSize);
    }

    internal async Task OpenFileAsync(string session = "unit")
    {
        Assert.IsType<Rattach>(await SendAsync(
            NinePMessage.NewMsgTattach(new Tattach(1, 1, NinePConstants.NoFid, "user", "/")), session));
        Assert.IsType<Rwalk>(await SendAsync(
            NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "file" })), session));
        Assert.IsType<Ropen>(await SendAsync(
            NinePMessage.NewMsgTopen(new Topen(3, 2, NinePConstants.ORDWR)), session));
    }

    internal async Task<object> SendAsync(NinePMessage request, string session = "unit", NinePDialect dialect = NinePDialect.NineP2000)
        => await Dispatcher.DispatchAsync(session, request, dialect).WaitAsync(TimeSpan.FromSeconds(1));
}

public sealed class TestAttachResolver : IDistributedNamespaceAttachResolver
{
    public string Group { get; set; } = "group";
    public ResourceHandle Root { get; set; } = GatewayTestContext.Root;
    public bool Deny { get; set; }
    public long ProcessId { get; set; } = 1;
    public string User { get; set; } = "user";

    public ValueTask<DistributedNamespaceAttach> ResolveAsync(
        string sessionId, Tattach request, NinePDialect dialect, X509Certificate2? certificate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Deny)
        {
            throw new UnauthorizedAccessException("attach denied");
        }

        return ValueTask.FromResult(new DistributedNamespaceAttach(Group, ProcessId, User, Root));
    }
}

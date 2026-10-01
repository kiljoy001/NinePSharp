namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class FileStatFixture : IFileStatOperations, IAsyncDisposable
{
    internal FileSyscallFixture Files { get; } = new();
    internal DirectoryStatOperations Codec { get; }
    internal Plan9FileSyscalls Calls { get; }
    internal FileStatOperations Adapter { get; }
    internal List<(ResourceHandle Resource, ResourceOpenHandle? Open, uint Count)> Requests { get; } = new();
    internal List<(ResourceHandle Resource, ResourceOpenHandle? Open, byte[] Stat, ResourceOperationContext Context)> Updates { get; } = new();
    internal Func<ResourceHandle, uint, ValueTask<ReadOnlyMemory<byte>>>? Reply { get; set; }
    internal Func<ResourceHandle, byte[], ResourceOperationContext, ValueTask<uint>>? UpdateReply { get; set; }

    internal FileStatFixture()
    {
        var devices = new[] { new DirectoryDeviceBinding(7, 0, "memory-data", "root"), new DirectoryDeviceBinding(7, 1, "memory-data", "mounted") };
        Codec = new(Files.Resources, devices);
        Adapter = new(Files.Resources, devices);
        Calls = ForProcess(Files.Process);
    }

    internal Plan9FileSyscalls ForProcess(VProcess process)
        => new(process, Files.Plane, Files.Context, fileStats: this);

    public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
    {
        AssertUncancelled(cancellationToken);
        Requests.Add((resource, null, count));
        return Reply is null ? Adapter.StatAsync(resource, count, cancellationToken) : Reply(resource, count);
    }

    public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken)
    {
        AssertUncancelled(cancellationToken);
        Requests.Add((handle.Resource, handle, count));
        return Reply is null ? Adapter.StatAsync(handle, count, cancellationToken) : Reply(handle.Resource, count);
    }

    public ValueTask<uint> WStatAsync(ResourceHandle resource, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
    {
        AssertUncancelled(cancellationToken);
        byte[] owned = stat.ToArray();
        Updates.Add((resource, null, owned, context));
        return UpdateReply is null
            ? Adapter.WStatAsync(resource, owned, context, cancellationToken)
            : UpdateReply(resource, owned, context);
    }

    public ValueTask<uint> WStatAsync(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
    {
        AssertUncancelled(cancellationToken);
        byte[] owned = stat.ToArray();
        Updates.Add((handle.Resource, handle, owned, context));
        return UpdateReply is null
            ? Adapter.WStatAsync(handle, owned, context, cancellationToken)
            : UpdateReply(handle.Resource, owned, context);
    }

    internal byte[] Record(ResourceHandle resource, string name, int size = 80)
        => Codec.Encode(new ResourceStat(resource, name, 0x180, 17, 29, 37,
            new string('u', size - 49 - System.Text.Encoding.UTF8.GetByteCount(name)), "", ""));

    private static void AssertUncancelled(CancellationToken token)
        => Xunit.Assert.False(token.CanBeCanceled);

    public ValueTask DisposeAsync() => Files.DisposeAsync();
}

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Binds one durable Orleans process-group namespace to a 9P connection data plane.</summary>
public sealed class DistributedNamespaceDataPlane : INamespaceDataPlane
{
    private readonly string processGroupId;
    private readonly DistributedNamespaceOperations operations;

    /// <summary>Initializes a data plane for one process group.</summary>
    public DistributedNamespaceDataPlane(string processGroupId, DistributedNamespaceOperations operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processGroupId);
        this.processGroupId = processGroupId;
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    /// <inheritdoc/>
    public async ValueTask<NamespaceChannel> AttachAsync(
        ResourceHandle root,
        CancellationToken cancellationToken)
        => await operations.AttachAsync(processGroupId, root, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
        => await operations.WalkAsync(processGroupId, source, names, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ResourceOpenHandle> OpenAsync(
        NamespaceChannel channel,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => await operations.OpenAsync(channel, mode, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
        => await operations.ReadAsync(openHandle, offset, count, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken)
        => await operations.ReadDirectoryStatsAsync(processGroupId, channel, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => await operations.WriteAsync(openHandle, offset, data, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ResourceStat> StatAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken)
        => await operations.StatAsync(processGroupId, channel, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<NamespaceCreateResult> CreateAndOpenAsync(
        NamespaceChannel channel,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => await operations.CreateAndOpenAsync(
            processGroupId,
            channel,
            name,
            permissions,
            mode,
            context,
            cancellationToken);

    /// <inheritdoc/>
    public async ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => await operations.ClunkAsync(openHandle, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(
        NamespaceChannel channel,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => await operations.RemoveAsync(channel, openHandle, context, cancellationToken);
}

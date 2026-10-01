namespace NinePSharp.Namespaces;

/// <summary>Namespace and resource operations needed by one 9P connection.</summary>
public interface INamespaceDataPlane
{
    /// <summary>Attaches and crosses a mount at the supplied root.</summary>
    ValueTask<NamespaceChannel> AttachAsync(ResourceHandle root, CancellationToken cancellationToken);

    /// <summary>Walks from an existing channel.</summary>
    ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken);

    /// <summary>Opens the resource selected by a channel.</summary>
    ValueTask<ResourceOpenHandle> OpenAsync(
        NamespaceChannel channel,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads an open resource.</summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata for the concatenated visible directory.</summary>
    ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken);

    /// <summary>Writes an open resource.</summary>
    ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata through the namespace.</summary>
    ValueTask<ResourceStat> StatAsync(NamespaceChannel channel, CancellationToken cancellationToken);

    /// <summary>Reads metadata for an already opened resource.</summary>
    ValueTask<ResourceStat> StatAsync(ResourceOpenHandle openHandle, CancellationToken cancellationToken);

    /// <summary>Creates in the selected union member and opens the child.</summary>
    ValueTask<NamespaceCreateResult> CreateAndOpenAsync(
        NamespaceChannel channel,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Closes provider-owned open state.</summary>
    ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Removes the selected resource.</summary>
    ValueTask RemoveAsync(
        NamespaceChannel channel,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

/// <summary>The opened child and replacement channel produced by a successful create.</summary>
public sealed record NamespaceCreateResult(NamespaceChannel Channel, ResourceOpenHandle OpenHandle);

/// <summary>Runs a connection against one live in-memory mount table.</summary>
public sealed class LocalNamespaceDataPlane : INamespaceDataPlane
{
    private readonly NamespaceNavigator navigator;
    private readonly IResourceDataOperations resources;

    /// <summary>Initializes a local namespace data plane.</summary>
    public LocalNamespaceDataPlane(MountTable mounts, IResourceDataOperations resources)
    {
        this.resources = resources; // NamespaceNavigator validates this dependency below.
        navigator = new NamespaceNavigator(mounts, resources);
    }

    /// <inheritdoc/>
    public ValueTask<NamespaceChannel> AttachAsync(ResourceHandle root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(navigator.Attach(root));
    }

    /// <inheritdoc/>
    public ValueTask<NamespaceWalkResult> WalkAsync(
        NamespaceChannel source,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
        => navigator.WalkAsync(source, names, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ResourceOpenHandle> OpenAsync(
        NamespaceChannel channel,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => resources.OpenAsync(channel.Current, mode, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
        => resources.ReadAsync(openHandle, offset, count, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ResourceDirectoryEntry> entries = await navigator.ReadDirectoryAsync(channel, cancellationToken);
        var result = new List<ResourceStat>(entries.Count);
        foreach (ResourceDirectoryEntry entry in entries)
        {
            ResourceStat stat = await resources.StatAsync(entry.Handle, cancellationToken);
            result.Add(stat with { Name = entry.Name });
        }

        return result;
    }

    /// <inheritdoc/>
    public ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => resources.WriteAsync(openHandle, offset, data, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ResourceStat> StatAsync(
        NamespaceChannel channel,
        CancellationToken cancellationToken)
    {
        ResourceStat stat = await resources.StatAsync(channel.Current, cancellationToken);
        string visibleName = channel.Frames[^1].Name;
        return stat with { Name = visibleName };
    }

    /// <inheritdoc/>
    public ValueTask<ResourceStat> StatAsync(
        ResourceOpenHandle openHandle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openHandle);
        return resources.StatAsync(openHandle.Resource, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<NamespaceCreateResult> CreateAndOpenAsync(
        NamespaceChannel channel,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        ResourceOpenHandle opened = await resources.CreateAndOpenAsync(
            navigator.SelectCreateTarget(channel),
            name,
            permissions,
            mode,
            context,
            cancellationToken);
        return new NamespaceCreateResult(
            navigator.EnterCreated(channel, name, opened.Resource),
            opened);
    }

    /// <inheritdoc/>
    public ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => resources.ClunkAsync(openHandle, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask RemoveAsync(
        NamespaceChannel channel,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => resources.RemoveAsync(channel.Current, openHandle, context, cancellationToken);
}

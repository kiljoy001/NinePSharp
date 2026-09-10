using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class MemoryDataResources : IResourceDataOperations
{
    private readonly Dictionary<ResourceIdentity, Node> nodes = new();
    private readonly Dictionary<ResourceOperationId, object?> completed = new();
    private ulong nextPath;

    internal bool FailClunk { get; set; }

    internal bool AdvanceVersionOnOpen { get; set; }

    internal int ClunkCount { get; private set; }

    internal ResourceHandle Directory(string device, params string[] children)
    {
        ResourceHandle root = Add(device, "/", true);
        foreach (string child in children)
        {
            AddChild(root, child, false);
        }

        return root;
    }

    public ValueTask<ResourceHandle?> WalkAsync(
        ResourceHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nodes[directory.Identity].Children.TryGetValue(name, out ResourceHandle? child);
        return ValueTask.FromResult(child);
    }

    public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ResourceDirectoryEntry> entries = nodes[directory.Identity].Children
            .Select(pair => new ResourceDirectoryEntry(pair.Key, pair.Value))
            .ToArray();
        return ValueTask.FromResult(entries);
    }

    public ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AddChild(directory, name, directoryEntry));
    }

    public ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceHandle opened = AdvanceVersionOnOpen ? resource with { Version = resource.Version + 1 } : resource;
        return ValueTask.FromResult(new ResourceOpenHandle(
            opened,
            OperationKey(context.OperationId),
            mode,
            0));
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] data = nodes[openHandle.Resource.Identity].Data;
        if (offset >= (ulong)data.Length)
        {
            return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        }

        int available = data.Length - checked((int)offset);
        int length = Math.Min(available, checked((int)count));
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory(checked((int)offset), length));
    }

    public ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completed.TryGetValue(context.OperationId, out object? prior))
        {
            return ValueTask.FromResult((uint)prior!);
        }

        Node node = nodes[openHandle.Resource.Identity];
        int start = checked((int)offset);
        int required = checked(start + data.Length);
        if (node.Data.Length < required)
        {
            Array.Resize(ref node.Data, required);
        }

        data.CopyTo(node.Data.AsMemory(start));
        uint count = checked((uint)data.Length);
        completed.Add(context.OperationId, count);
        return ValueTask.FromResult(count);
    }

    public ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Node node = nodes[resource.Identity];
        return ValueTask.FromResult(new ResourceStat(
            resource,
            node.Name,
            resource.IsDirectory ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755 : NinePConstants.Mode0644,
            0,
            0,
            checked((ulong)node.Data.Length),
            "owner",
            "owner",
            "owner"));
    }

    public ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completed.TryGetValue(context.OperationId, out object? prior))
        {
            return ValueTask.FromResult((ResourceOpenHandle)prior!);
        }

        bool isDirectory = (permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
        ResourceHandle child = AddChild(directory, name, isDirectory);
        var result = new ResourceOpenHandle(child, OperationKey(context.OperationId), mode, 0);
        completed.Add(context.OperationId, result);
        return ValueTask.FromResult(result);
    }

    public ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailClunk)
        {
            throw new IOException("clunk failed");
        }

        if (completed.TryAdd(context.OperationId, null))
        {
            ClunkCount++;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completed.ContainsKey(context.OperationId))
        {
            return ValueTask.CompletedTask;
        }

        Node node = nodes[resource.Identity];
        if (node.Parent is not null)
        {
            nodes[node.Parent.Identity].Children.Remove(node.Name);
        }

        completed.Add(context.OperationId, null);
        return ValueTask.CompletedTask;
    }

    private static string OperationKey(ResourceOperationId operation)
        => $"{operation.SessionId}:{operation.Sequence}";

    private ResourceHandle AddChild(ResourceHandle parent, string name, bool directory)
    {
        ResourceHandle child = Add(parent.Identity.Device, name, directory, parent);
        nodes[parent.Identity].Children.Add(name, child);
        return child;
    }

    private ResourceHandle Add(string device, string name, bool directory, ResourceHandle? parent = null)
    {
        var identity = new ResourceIdentity("memory-data", device, ++nextPath);
        var handle = new ResourceHandle(identity, directory ? QidType.QTDIR : QidType.QTFILE);
        nodes.Add(identity, new Node(name, parent));
        return handle;
    }

    private sealed class Node
    {
        internal Node(string name, ResourceHandle? parent)
        {
            Name = name;
            Parent = parent;
        }

        internal string Name { get; }

        internal ResourceHandle? Parent { get; }

        internal Dictionary<string, ResourceHandle> Children { get; } = new(StringComparer.Ordinal);

        internal byte[] Data = Array.Empty<byte>();
    }
}

using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class MemoryResources : IResourceOperations
{
    private readonly Dictionary<ResourceIdentity, Node> nodes = new();
    private ulong nextPath;

    internal ResourceHandle Directory(string device, params string[] children)
    {
        ResourceHandle root = Add(device, true);
        foreach (string child in children)
        {
            AddChild(root, child, false);
        }

        return root;
    }

    internal ResourceHandle File(string device) => Add(device, false);

    internal ResourceHandle AddChild(ResourceHandle parent, string name, bool directory)
    {
        Node parentNode = nodes[parent.Identity];
        ResourceHandle child = Add(parent.Identity.Device, directory);
        parentNode.Children.Add(name, child);
        return child;
    }

    internal bool Contains(ResourceHandle parent, string name)
        => nodes[parent.Identity].Children.ContainsKey(name);

    public ValueTask<ResourceHandle?> WalkAsync(
        ResourceHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Node node = nodes[directory.Identity];
        node.Children.TryGetValue(name, out ResourceHandle? child);
        return ValueTask.FromResult(child);
    }

    public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ResourceDirectoryEntry> result = nodes[directory.Identity].Children
            .Select(pair => new ResourceDirectoryEntry(pair.Key, pair.Value))
            .ToArray();
        return ValueTask.FromResult(result);
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

    private ResourceHandle Add(string device, bool directory)
    {
        var identity = new ResourceIdentity("memory", device, ++nextPath);
        var handle = new ResourceHandle(identity, directory ? QidType.QTDIR : QidType.QTFILE);
        nodes.Add(identity, new Node(handle));
        return handle;
    }

    private sealed class Node
    {
        internal Node(ResourceHandle handle)
        {
            Handle = handle;
        }

        internal ResourceHandle Handle { get; }

        internal Dictionary<string, ResourceHandle> Children { get; } = new(StringComparer.Ordinal);
    }
}

using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces.Authorization.Tests.Support;

/// <summary>
/// An in-memory provider with owner, group and mode per node, a parent relation that can be
/// withheld or corrupted, moves, and a log of every call that reached it.
/// </summary>
internal sealed class TreeResources : IResourceDataOperations, IResourceAncestry, IResourceWStatOperations, IResourceOpenStatOperations
{
    internal const string Provider = "tree";
    private readonly object gate = new();
    private readonly Dictionary<ResourceIdentity, Node> nodes = new();
    private readonly Dictionary<string, ResourceIdentity> paths = new(StringComparer.Ordinal);
    private readonly Dictionary<ResourceIdentity, ResourceIdentity> parentOverrides = new();
    private ulong nextPath;
    private ulong nextHandle;

    internal List<string> Calls { get; } = new();

    internal uint? LastCreatePermissions { get; private set; }

    public ValueTask<ResourceHandle?> GetParentAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("parent", resource);
            if (parentOverrides.TryGetValue(resource.Identity, out ResourceIdentity? forced))
            {
                return ValueTask.FromResult<ResourceHandle?>(nodes[forced].Handle);
            }

            ResourceIdentity? parent = nodes.TryGetValue(resource.Identity, out Node? node) ? node.Parent : null;
            return ValueTask.FromResult(parent is null ? null : nodes[parent].Handle);
        }
    }

    public ValueTask<ResourceHandle?> WalkAsync(ResourceHandle directory, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("walk", directory, name);
            return ValueTask.FromResult(nodes[directory.Identity].Children.TryGetValue(name, out ResourceIdentity? child)
                ? nodes[child].Handle
                : null);
        }
    }

    public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(ResourceHandle directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("readdir", directory);
            IReadOnlyList<ResourceDirectoryEntry> entries = nodes[directory.Identity].Children
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ResourceDirectoryEntry(pair.Key, nodes[pair.Value].Handle))
                .ToArray();
            return ValueTask.FromResult(entries);
        }
    }

    public ValueTask<ResourceHandle> CreateAsync(ResourceHandle directory, string name, bool directoryEntry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("create", directory, name);
            return ValueTask.FromResult(AddChild(directory, name, directoryEntry, directoryEntry ? 0x800001EDU : 0x1A4U, "owner").Handle);
        }
    }

    public ValueTask<ResourceOpenHandle> OpenAsync(ResourceHandle resource, byte mode, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("open", resource);
            return ValueTask.FromResult(new ResourceOpenHandle(nodes[resource.Identity].Handle, $"open-{++nextHandle}", mode, 8192));
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(ResourceOpenHandle openHandle, ulong offset, uint count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("read", openHandle.Resource);
            byte[] data = nodes[openHandle.Resource.Identity].Data;
            int start = (int)Math.Min(offset, (ulong)data.Length);
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory(start, (int)Math.Min(count, (uint)(data.Length - start))));
        }
    }

    public ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("write", openHandle.Resource);
            nodes[openHandle.Resource.Identity].Data = data.ToArray();
            return ValueTask.FromResult((uint)data.Length);
        }
    }

    public ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Record("stat", resource);
            return ValueTask.FromResult(StatOf(nodes[resource.Identity]));
        }
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
        lock (gate)
        {
            Record("create", directory, name);
            LastCreatePermissions = permissions;
            bool isDirectory = (permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
            Node child = AddChild(directory, name, isDirectory, permissions, context.User);
            return ValueTask.FromResult(new ResourceOpenHandle(child.Handle, $"open-{++nextHandle}", mode, 8192));
        }
    }

    public ValueTask ClunkAsync(ResourceOpenHandle openHandle, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Record("clunk", openHandle.Resource);
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
        lock (gate)
        {
            Record("remove", resource);
            Node node = nodes[resource.Identity];
            if (node.Parent is not null)
            {
                nodes[node.Parent].Children.Remove(node.Name);
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<ResourceStat> StatOpenAsync(ResourceOpenHandle handle, CancellationToken cancellationToken)
        => StatAsync(handle.Resource, cancellationToken);

    public ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Record("wstat", resource);
        }

        return ValueTask.FromResult(0U);
    }

    public ValueTask<uint> WStatOpenAsync(
        ResourceOpenHandle handle,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => WStatAsync(handle.Resource, stat, context, cancellationToken);

    internal ResourceHandle Add(string path, bool directory, string owner, string group, uint mode)
    {
        lock (gate)
        {
            var identity = new ResourceIdentity(Provider, "disk", ++nextPath);
            var handle = new ResourceHandle(identity, directory ? QidType.QTDIR : QidType.QTFILE);
            string name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..];
            ResourceIdentity? parent = path == "/" ? null : paths[ParentPath(path)];
            nodes.Add(identity, new Node(handle, name, parent) { Owner = owner, Group = group, Mode = mode });
            if (parent is not null)
            {
                nodes[parent].Children.Add(name, identity);
            }

            paths.Add(path, identity);
            return handle;
        }
    }

    internal ResourceHandle Handle(string path)
    {
        lock (gate)
        {
            return nodes[paths[path]].Handle;
        }
    }

    internal void SetMode(string path, uint mode)
    {
        lock (gate)
        {
            nodes[paths[path]].Mode = (nodes[paths[path]].Mode & (uint)NinePConstants.FileMode9P.DMDIR) | mode;
        }
    }

    internal void SetOwner(string path, string owner)
    {
        lock (gate)
        {
            nodes[paths[path]].Owner = owner;
        }
    }

    internal void Move(string path, string destination)
    {
        lock (gate)
        {
            ResourceIdentity identity = paths[path];
            Node node = nodes[identity];
            nodes[node.Parent!].Children.Remove(node.Name);
            ResourceIdentity target = paths[ParentPath(destination)];
            node.Name = destination[(destination.LastIndexOf('/') + 1)..];
            node.Parent = target;
            nodes[target].Children.Add(node.Name, identity);
            paths.Remove(path);
            paths.Add(destination, identity);
        }
    }

    internal void ReportParent(string child, string parent)
    {
        lock (gate)
        {
            parentOverrides[paths[child]] = paths[parent];
        }
    }

    internal int CallsMatching(string prefix)
    {
        lock (gate)
        {
            return Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    internal int MutatingOrOpeningCalls()
    {
        lock (gate)
        {
            return Calls.Count(call => call.StartsWith("open ", StringComparison.Ordinal) ||
                call.StartsWith("write ", StringComparison.Ordinal) || call.StartsWith("read ", StringComparison.Ordinal) ||
                call.StartsWith("create ", StringComparison.Ordinal) || call.StartsWith("remove ", StringComparison.Ordinal) ||
                call.StartsWith("readdir ", StringComparison.Ordinal) || call.StartsWith("stat ", StringComparison.Ordinal));
        }
    }

    internal ResourceOpenHandle OpenDirect(string path, byte mode)
    {
        lock (gate)
        {
            return new ResourceOpenHandle(nodes[paths[path]].Handle, $"direct-{++nextHandle}", mode, 8192);
        }
    }

    /// <summary>Reads mode, owner and group without logging, for the layer's own permission checks.</summary>
    internal ResourceStat Peek(string path)
    {
        lock (gate)
        {
            return StatOf(nodes[paths[path]]);
        }
    }

    private static string ParentPath(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash == 0 ? "/" : path[..slash];
    }

    private static ResourceStat StatOf(Node node)
        => new(node.Handle, node.Name, node.Mode, 0, 0, (ulong)node.Data.Length, node.Owner, node.Group, node.Owner);

    private Node AddChild(ResourceHandle directory, string name, bool isDirectory, uint permissions, string owner)
    {
        if (nodes[directory.Identity].Children.ContainsKey(name))
        {
            throw new ResourceCreateRejectedException("file already exists");
        }

        var identity = new ResourceIdentity(Provider, "disk", ++nextPath);
        var handle = new ResourceHandle(identity, isDirectory ? QidType.QTDIR : QidType.QTFILE);
        var node = new Node(handle, name, directory.Identity)
        {
            Owner = owner,
            Group = nodes[directory.Identity].Group,
            Mode = permissions,
        };
        nodes.Add(identity, node);
        nodes[directory.Identity].Children.Add(name, identity);
        return node;
    }

    private void Record(string operation, ResourceHandle resource, string? name = null)
    {
        string path = paths.FirstOrDefault(pair => pair.Value == resource.Identity).Key ?? resource.Identity.ToString();
        Calls.Add(name is null ? $"{operation} {path}" : $"{operation} {path} {name}");
    }

    private sealed class Node(ResourceHandle handle, string name, ResourceIdentity? parent)
    {
        internal ResourceHandle Handle { get; } = handle;

        internal string Name { get; set; } = name;

        internal ResourceIdentity? Parent { get; set; } = parent;

        internal string Owner { get; set; } = "glenda";

        internal string Group { get; set; } = "sys";

        internal uint Mode { get; set; }

        internal Dictionary<string, ResourceIdentity> Children { get; } = new(StringComparer.Ordinal);

        internal byte[] Data { get; set; } = "contents"u8.ToArray();
    }
}

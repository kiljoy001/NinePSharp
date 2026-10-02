using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class MemoryDataResources : IResourceDataOperations, IResourceOpenStatOperations, IResourceWStatOperations
{
    private readonly object gate = new();
    private readonly Dictionary<ResourceIdentity, Node> nodes = new();
    private readonly Dictionary<ResourceOperationId, object?> completed = new();
    private ulong nextPath;

    internal Func<Task>? BeforeWalk { get; set; }

    internal Func<Task>? AfterCreate { get; set; }

    internal bool FailClunk { get; set; }

    internal bool AdvanceVersionOnOpen { get; set; }

    internal int ClunkCount { get; private set; }

    public async ValueTask<ResourceHandle?> WalkAsync(
        ResourceHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeWalk is not null)
        {
            await BeforeWalk();
        }

        lock (gate)
        {
            nodes[directory.Identity].Children.TryGetValue(name, out ResourceHandle? child);
            return child;
        }
    }

    public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IReadOnlyList<ResourceDirectoryEntry> entries = nodes[directory.Identity].Children
                .Select(pair => new ResourceDirectoryEntry(pair.Key, pair.Value))
                .ToArray();
            return ValueTask.FromResult(entries);
        }
    }

    public ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(AddChild(directory, name, directoryEntry));
        }
    }

    public ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Node node = nodes[resource.Identity];
            if ((mode & NinePConstants.OTRUNC) != 0)
            {
                if (resource.IsDirectory)
                {
                    throw new IOException("cannot truncate directory");
                }

                node.Data = Array.Empty<byte>();
            }

            ResourceHandle opened = AdvanceVersionOnOpen ? resource with { Version = resource.Version + 1 } : resource;
            return ValueTask.FromResult(new ResourceOpenHandle(
                opened,
                OperationKey(context.OperationId),
                mode,
                0));
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            byte[] data = nodes[openHandle.Resource.Identity].Data;
            if (offset >= (ulong)data.Length)
            {
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
            }

            int available = data.Length - checked((int)offset);
            int length = Math.Min(available, checked((int)count));
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory(checked((int)offset), length));
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
            if (completed.TryGetValue(context.OperationId, out object? prior))
            {
                return ValueTask.FromResult((uint)prior!);
            }

            Node node = nodes[openHandle.Resource.Identity];
            int start = checked((int)offset);
            int required = checked(start + data.Length);
            if (node.Data.Length < required)
            {
                byte[] grown = node.Data;
                Array.Resize(ref grown, required);
                node.Data = grown;
            }

            data.CopyTo(node.Data.AsMemory(start));
            uint count = checked((uint)data.Length);
            completed.Add(context.OperationId, count);
            return ValueTask.FromResult(count);
        }
    }

    public ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Node node = nodes[resource.Identity];
            return ValueTask.FromResult(new ResourceStat(
                resource,
                node.Name,
                node.Mode,
                0,
                node.ModificationTime,
                checked((ulong)node.Data.Length),
                node.User,
                node.Group,
                node.LastModifier));
        }
    }

    public ValueTask<ResourceStat> StatOpenAsync(ResourceOpenHandle handle, CancellationToken cancellationToken)
        => StatAsync(handle.Resource, cancellationToken);

    public ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => WStatCoreAsync(resource, stat, context, cancellationToken);

    public ValueTask<uint> WStatOpenAsync(
        ResourceOpenHandle handle,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => WStatCoreAsync(handle.Resource, stat, context, cancellationToken);

    public async ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        ResourceOpenHandle opened = await CreateAndOpenCoreAsync(directory, name, permissions, mode, context, cancellationToken);
        if (AfterCreate is not null)
        {
            await AfterCreate();
        }

        return opened;
    }

    public ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
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
    }

    internal ResourceHandle Directory(string device, params string[] children)
    {
        ResourceHandle root = Add(device, "/", true);
        foreach (string child in children)
        {
            AddChild(root, child, false);
        }

        return root;
    }

    private static string OperationKey(ResourceOperationId operation)
        => $"{operation.SessionId}:{operation.Sequence}";

    private ValueTask<uint> WStatCoreAsync(
        ResourceHandle resource,
        ResourceWStat stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (completed.TryGetValue(context.OperationId, out object? prior))
            {
                return ValueTask.FromResult((uint)prior!);
            }

            Node node = nodes[resource.Identity];
            if (stat.Type != ushort.MaxValue || stat.Device != uint.MaxValue
                || (byte)stat.Qid.Type != byte.MaxValue || stat.Qid.Version != uint.MaxValue
                || stat.Qid.Path != ulong.MaxValue || stat.AccessTime != uint.MaxValue
                || stat.User.Length != 0 || stat.LastModifier.Length != 0)
            {
                throw new ResourceWStatRejectedException("wstat attempts to change protected metadata");
            }

            if (stat.Mode != uint.MaxValue
                && ((stat.Mode ^ node.Mode) & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
            {
                throw new ResourceWStatRejectedException("wstat cannot change DMDIR");
            }

            if (resource.IsDirectory && stat.Length != ulong.MaxValue && stat.Length != 0)
            {
                throw new ResourceWStatRejectedException("directory length must be zero");
            }

            if (stat.Length != ulong.MaxValue && stat.Length > int.MaxValue)
            {
                throw new ResourceWStatRejectedException("file length exceeds the memory provider limit");
            }

            ResourceHandle? parent = node.Parent;
            if (stat.Name.Length != 0)
            {
                if (parent is null || stat.Name is "/" or "." or ".." || stat.Name.Contains('/'))
                {
                    throw new ResourceWStatRejectedException("invalid rename");
                }

                Node parentNode = nodes[parent.Identity];
                if (parentNode.Children.TryGetValue(stat.Name, out ResourceHandle? existing)
                    && existing.Identity != resource.Identity)
                {
                    throw new ResourceWStatRejectedException("rename target exists");
                }
            }

            if (stat.Name.Length != 0 && parent is not null)
            {
                Node parentNode = nodes[parent.Identity];
                parentNode.Children.Remove(node.Name);
                parentNode.Children.Add(stat.Name, resource);
                node.Name = stat.Name;
            }

            if (stat.Mode != uint.MaxValue)
            {
                node.Mode = stat.Mode;
            }

            if (stat.ModificationTime != uint.MaxValue)
            {
                node.ModificationTime = stat.ModificationTime;
            }

            if (stat.Group.Length != 0)
            {
                node.Group = stat.Group;
            }

            if (stat.Length != ulong.MaxValue && !resource.IsDirectory)
            {
                byte[] resized = node.Data;
                Array.Resize(ref resized, checked((int)stat.Length));
                node.Data = resized;
            }

            uint result = stat.EncodedLength;
            completed.Add(context.OperationId, result);
            return ValueTask.FromResult(result);
        }
    }

    private ValueTask<ResourceOpenHandle> CreateAndOpenCoreAsync(
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
            if (completed.TryGetValue(context.OperationId, out object? prior))
            {
                return ValueTask.FromResult((ResourceOpenHandle)prior!);
            }

            bool isDirectory = (permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
            ResourceHandle child = AddChild(directory, name, isDirectory);
            nodes[child.Identity].Mode = permissions & (nodes[directory.Identity].Mode | ~NinePConstants.Mode0777);
            nodes[child.Identity].User = context.User;
            var result = new ResourceOpenHandle(child, OperationKey(context.OperationId), mode, 0);
            completed.Add(context.OperationId, result);
            return ValueTask.FromResult(result);
        }
    }

    private ResourceHandle AddChild(ResourceHandle parent, string name, bool directory)
    {
        if (nodes[parent.Identity].Children.ContainsKey(name))
        {
            throw new ResourceCreateRejectedException("file already exists");
        }

        ResourceHandle child = Add(parent.Identity.Device, name, directory, parent);
        nodes[parent.Identity].Children.Add(name, child);
        return child;
    }

    private ResourceHandle Add(string device, string name, bool directory, ResourceHandle? parent = null)
    {
        var identity = new ResourceIdentity("memory-data", device, ++nextPath);
        var handle = new ResourceHandle(identity, directory ? QidType.QTDIR : QidType.QTFILE);
        nodes.Add(identity, new Node(name, parent)
        {
            Mode = directory ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755 : NinePConstants.Mode0644,
        });
        return handle;
    }

    private sealed class Node
    {
        internal Node(string name, ResourceHandle? parent)
        {
            Name = name;
            Parent = parent;
        }

        internal byte[] Data { get; set; } = Array.Empty<byte>();

        internal string Name { get; set; }

        internal ResourceHandle? Parent { get; }

        internal uint Mode { get; set; }

        internal string User { get; set; } = "owner";

        internal string Group { get; set; } = "owner";

        internal string LastModifier { get; set; } = "owner";

        internal uint ModificationTime { get; set; }

        internal Dictionary<string, ResourceHandle> Children { get; } = new(StringComparer.Ordinal);
    }
}

using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

public sealed class TestMountableResourceGrain : Grain, IWStatResourceGrain, ITestMountableResourceGrain
{
    private readonly IPersistentState<TestResourceState> state;
    private int nextReadDelay;
    private bool loseNextWStatReply;

    public TestMountableResourceGrain(
        [PersistentState("test-resource")] IPersistentState<TestResourceState> state)
    {
        this.state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (!state.State.Initialized)
        {
            state.State.Initialized = true;
            state.State.NextPath = 2;
            state.State.Nodes[1] = new TestResourceNode
            {
                Name = "/",
                Directory = true,
                Mode = (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755,
            };
            state.State.Nodes[2] = new TestResourceNode { Name = "job", Mode = NinePConstants.Mode0644 };
            state.State.Nodes[1].Children["job"] = 2;
        }

        state.State.Activations++;
        await state.WriteStateAsync(cancellationToken);

        await base.OnActivateAsync(cancellationToken);
    }

    public Task<ResourceHandleModel?> WalkAsync(ResourceHandleModel directory, string name)
    {
        TestResourceNode node = RequireNode(directory.Identity.Path);
        ResourceHandleModel? result = node.Children.TryGetValue(name, out ulong path)
            ? Handle(path)
            : null;
        return Task.FromResult(result);
    }

    public Task<ResourceDirectoryEntryModel[]> ReadDirectoryAsync(ResourceHandleModel directory)
    {
        ResourceDirectoryEntryModel[] result = RequireNode(directory.Identity.Path).Children
            .Select(pair => new ResourceDirectoryEntryModel(pair.Key, Handle(pair.Value)))
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<ResourceHandleModel> CreateAsync(
        ResourceHandleModel directory,
        string name,
        bool directoryEntry)
    {
        ResourceHandleModel result = AddChild(directory, name, directoryEntry);
        state.State.Mutations++;
        await state.WriteStateAsync();
        return result;
    }

    public async Task<ResourceOpenHandleModel> OpenAsync(
        ResourceHandleModel resource,
        byte mode,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint("open", resource.Identity.Path, mode);
        if (TryReplay(key, fingerprint, out TestCompletedOperation? prior))
        {
            return OpenResult(prior!);
        }

        TestResourceNode node = RequireNode(resource.Identity.Path);
        if ((mode & NinePConstants.OTRUNC) != 0)
        {
            node.Data = Array.Empty<byte>();
            node.Version++;
            state.State.Mutations++;
        }

        var completed = new TestCompletedOperation
        {
            Fingerprint = fingerprint,
            ResourcePath = resource.Identity.Path,
            HandleId = key,
            Mode = mode,
        };
        state.State.Completed.Add(key, completed);
        await state.WriteStateAsync();
        return OpenResult(completed);
    }

    public async Task<byte[]> ReadAsync(ResourceOpenHandleModel openHandle, ulong offset, uint count)
    {
        if (nextReadDelay > 0)
        {
            int delay = nextReadDelay;
            nextReadDelay = 0;
            await Task.Delay(delay);
        }

        TestResourceNode node = RequireNode(openHandle.Resource.Identity.Path);
        if (node.Directory)
        {
            ulong position = 0;
            var records = new List<byte>();
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            foreach (ulong path in node.Children.Values)
            {
                ResourceStatModel metadata = await StatAsync(Handle(path));
                var stat = new NinePSharp.Messages.Stat(
                    0,
                    7,
                    0,
                    metadata.Resource.ToDomain().Qid,
                    metadata.Mode,
                    metadata.AccessTime,
                    metadata.ModificationTime,
                    metadata.Length,
                    metadata.Name,
                    metadata.User,
                    metadata.Group,
                    metadata.LastModifier);
                if (position < offset)
                {
                    position += stat.Size;
                    if (position > offset)
                    {
                        throw new ResourceDirectoryRejectedGrainException("invalid directory offset");
                    }

                    continue;
                }

                if ((long)records.Count + stat.Size > count)
                {
                    if (records.Count == 0)
                    {
                        throw new ResourceDirectoryRejectedGrainException("directory buffer too small");
                    }

                    break;
                }

                var encoded = new byte[stat.Size];
                int written = 0;
                stat.WriteTo(encoded, ref written);
                records.AddRange(encoded);
                position += stat.Size;
            }

            return records.ToArray();
        }

        byte[] data = node.Data;
        if (offset >= (ulong)data.Length)
        {
            return Array.Empty<byte>();
        }

        int start = checked((int)offset);
        int length = Math.Min(data.Length - start, checked((int)count));
        return data.AsSpan(start, length).ToArray();
    }

    public async Task<uint> WriteAsync(
        ResourceOpenHandleModel openHandle,
        ulong offset,
        byte[] data,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint(
            "write",
            openHandle.Resource.Identity.Path,
            openHandle.HandleId,
            offset,
            Convert.ToHexString(SHA256.HashData(data)));
        if (TryReplay(key, fingerprint, out TestCompletedOperation? prior))
        {
            return prior!.Count;
        }

        TestResourceNode node = RequireNode(openHandle.Resource.Identity.Path);
        int start = checked((int)offset);
        int required = checked(start + data.Length);
        if (node.Data.Length < required)
        {
            byte[] resized = node.Data;
            Array.Resize(ref resized, required);
            node.Data = resized;
        }

        data.CopyTo(node.Data, start);
        node.Version++;
        state.State.Mutations++;
        uint written = checked((uint)data.Length);
        state.State.Completed.Add(key, new TestCompletedOperation
        {
            Fingerprint = fingerprint,
            Count = written,
        });
        await state.WriteStateAsync();
        return written;
    }

    public Task<ResourceStatModel> StatAsync(ResourceHandleModel resource)
    {
        TestResourceNode node = RequireNode(resource.Identity.Path);
        return Task.FromResult(new ResourceStatModel(
            Handle(resource.Identity.Path),
            node.Name,
            node.Mode,
            0,
            node.ModificationTime,
            checked((ulong)node.Data.Length),
            "glenda",
            "glenda",
            "glenda"));
    }

    public Task<ResourceStatModel> StatOpenAsync(ResourceOpenHandleModel handle)
    {
        if (!state.State.Completed.Values.Any(operation => operation.HandleId == handle.HandleId
            && operation.ResourcePath == handle.Resource.Identity.Path))
        {
            throw new IOException("unknown open handle");
        }

        return StatAsync(handle.Resource);
    }

    public Task<uint> WStatAsync(
        ResourceHandleModel resource,
        ResourceWStatModel stat,
        ResourceOperationContextModel context)
        => WStatCoreAsync(resource, null, stat, context);

    public Task<uint> WStatOpenAsync(
        ResourceOpenHandleModel handle,
        ResourceWStatModel stat,
        ResourceOperationContextModel context)
    {
        if (!state.State.Completed.Values.Any(operation => operation.HandleId == handle.HandleId
            && operation.ResourcePath == handle.Resource.Identity.Path))
        {
            throw new ResourceWStatRejectedGrainException("unknown open handle");
        }

        return WStatCoreAsync(handle.Resource, handle.HandleId, stat, context);
    }

    public async Task<ResourceOpenHandleModel> CreateAndOpenAsync(
        ResourceHandleModel directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint("create", directory.Identity.Path, name, permissions, mode);
        if (TryReplay(key, fingerprint, out TestCompletedOperation? prior))
        {
            return OpenResult(prior!);
        }

        bool isDirectory = (permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
        ResourceHandleModel child = AddChild(directory, name, isDirectory);
        state.State.Mutations++;
        var completed = new TestCompletedOperation
        {
            Fingerprint = fingerprint,
            ResourcePath = child.Identity.Path,
            HandleId = key,
            Mode = mode,
        };
        state.State.Completed.Add(key, completed);
        await state.WriteStateAsync();
        return OpenResult(completed);
    }

    public async Task ClunkAsync(
        ResourceOpenHandleModel openHandle,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint("clunk", openHandle.HandleId);
        if (TryReplay(key, fingerprint, out _))
        {
            return;
        }

        state.State.Clunks++;
        state.State.Completed.Add(key, new TestCompletedOperation { Fingerprint = fingerprint });
        await state.WriteStateAsync();
    }

    public async Task RemoveAsync(
        ResourceHandleModel resource,
        ResourceOpenHandleModel? openHandle,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint("remove", resource.Identity.Path, openHandle?.HandleId ?? string.Empty);
        if (TryReplay(key, fingerprint, out _))
        {
            return;
        }

        foreach (TestResourceNode parent in state.State.Nodes.Values)
        {
            string? childName = parent.Children.FirstOrDefault(pair => pair.Value == resource.Identity.Path).Key;
            if (childName is not null)
            {
                parent.Children.Remove(childName);
                break;
            }
        }

        state.State.Nodes.Remove(resource.Identity.Path);
        state.State.Mutations++;
        state.State.Completed.Add(key, new TestCompletedOperation { Fingerprint = fingerprint });
        await state.WriteStateAsync();
    }

    public Task<TestResourceDiagnostics> GetDiagnosticsAsync()
        => Task.FromResult(new TestResourceDiagnostics(
            state.State.Mutations,
            state.State.Clunks,
            state.State.Nodes[1].Children.Keys.Order().ToArray(),
            state.State.Activations,
            RuntimeIdentity));

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task DelayNextReadAsync(int milliseconds)
    {
        nextReadDelay = milliseconds;
        return Task.CompletedTask;
    }

    public Task LoseNextWStatReplyAsync()
    {
        loseNextWStatReply = true;
        return Task.CompletedTask;
    }

    private static string OperationKey(ResourceOperationIdModel operation)
        => $"{operation.SessionId.Length}:{operation.SessionId}:{operation.Sequence}";

    private static string Fingerprint(params object[] values)
        => string.Join('\0', values.Select(value => value.ToString()));

    private async Task<uint> WStatCoreAsync(
        ResourceHandleModel resource,
        string? openHandleId,
        ResourceWStatModel stat,
        ResourceOperationContextModel context)
    {
        string key = OperationKey(context.OperationId);
        string fingerprint = Fingerprint(
            "wstat",
            resource.Identity.Path,
            openHandleId ?? "path",
            stat.Type,
            stat.Device,
            (byte)stat.QidType,
            stat.QidVersion,
            stat.QidPath,
            stat.Mode,
            stat.AccessTime,
            stat.ModificationTime,
            stat.Length,
            stat.Name,
            stat.User,
            stat.Group,
            stat.LastModifier,
            stat.EncodedLength);
        if (TryReplay(key, fingerprint, out TestCompletedOperation? prior))
        {
            if (prior!.Rejected)
            {
                throw new ResourceWStatRejectedGrainException(prior.Error);
            }

            return prior.Count;
        }

        if (!state.State.Nodes.TryGetValue(resource.Identity.Path, out TestResourceNode? node))
        {
            throw await RecordWStatRejectionAsync(key, fingerprint, "file does not exist");
        }

        if (stat.Type != ushort.MaxValue || stat.Device != uint.MaxValue
            || (byte)stat.QidType != byte.MaxValue || stat.QidVersion != uint.MaxValue
            || stat.QidPath != ulong.MaxValue || stat.AccessTime != uint.MaxValue
            || stat.User.Length != 0 || stat.LastModifier.Length != 0)
        {
            throw await RecordWStatRejectionAsync(key, fingerprint, "wstat attempts to change protected metadata");
        }

        if (stat.Mode != uint.MaxValue
            && ((stat.Mode ^ node.Mode) & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            throw await RecordWStatRejectionAsync(key, fingerprint, "wstat cannot change DMDIR");
        }

        if (node.Directory && stat.Length != ulong.MaxValue && stat.Length != 0)
        {
            throw await RecordWStatRejectionAsync(key, fingerprint, "directory length must be zero");
        }

        if (stat.Length != ulong.MaxValue && stat.Length > int.MaxValue)
        {
            throw await RecordWStatRejectionAsync(key, fingerprint, "file length exceeds the test provider limit");
        }

        TestResourceNode? parent = state.State.Nodes.Values.SingleOrDefault(candidate =>
            candidate.Children.Values.Contains(resource.Identity.Path));
        if (stat.Name.Length != 0)
        {
            if (parent is null || stat.Name is "/" or "." or ".." || stat.Name.Contains('/'))
            {
                throw await RecordWStatRejectionAsync(key, fingerprint, "invalid rename");
            }

            if (parent.Children.TryGetValue(stat.Name, out ulong existing) && existing != resource.Identity.Path)
            {
                throw await RecordWStatRejectionAsync(key, fingerprint, "rename target exists");
            }
        }

        if (stat.Name.Length != 0 && parent is not null)
        {
            parent.Children.Remove(node.Name);
            parent.Children.Add(stat.Name, resource.Identity.Path);
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

        if (stat.Length != ulong.MaxValue && !node.Directory)
        {
            byte[] resized = node.Data;
            Array.Resize(ref resized, checked((int)stat.Length));
            node.Data = resized;
        }

        node.Version++;
        state.State.Mutations++;
        state.State.Completed.Add(key, new TestCompletedOperation { Fingerprint = fingerprint, Count = stat.EncodedLength });
        await state.WriteStateAsync();
        if (loseNextWStatReply)
        {
            loseNextWStatReply = false;
            throw new IOException("simulated lost wstat reply");
        }

        return stat.EncodedLength;
    }

    private async Task<ResourceWStatRejectedGrainException> RecordWStatRejectionAsync(
        string key,
        string fingerprint,
        string error)
    {
        state.State.Completed.Add(key, new TestCompletedOperation
        {
            Fingerprint = fingerprint,
            Rejected = true,
            Error = error,
        });
        await state.WriteStateAsync();
        return new ResourceWStatRejectedGrainException(error);
    }

    private bool TryReplay(
        string operationKey,
        string fingerprint,
        out TestCompletedOperation? completed)
    {
        if (!state.State.Completed.TryGetValue(operationKey, out completed))
        {
            return false;
        }

        if (!string.Equals(completed.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An operation identity was reused for a different request.");
        }

        return true;
    }

    private ResourceHandleModel AddChild(ResourceHandleModel parentHandle, string name, bool directoryEntry)
    {
        TestResourceNode parent = RequireNode(parentHandle.Identity.Path);
        if (parent.Children.ContainsKey(name))
        {
            throw new ResourceCreateRejectedGrainException("file already exists");
        }

        ulong path = ++state.State.NextPath;
        state.State.Nodes.Add(path, new TestResourceNode
        {
            Name = name,
            Directory = directoryEntry,
            Mode = directoryEntry
                ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755
                : NinePConstants.Mode0644,
        });
        parent.Children.Add(name, path);
        return Handle(path);
    }

    private TestResourceNode RequireNode(ulong path)
        => state.State.Nodes.TryGetValue(path, out TestResourceNode? node)
            ? node
            : throw new InvalidOperationException("file does not exist");

    private ResourceHandleModel Handle(ulong path)
    {
        TestResourceNode node = RequireNode(path);
        return new ResourceHandleModel(
            new ResourceIdentityModel("bdd-resource", this.GetPrimaryKeyString(), path),
            node.Directory ? QidType.QTDIR : QidType.QTFILE,
            node.Version);
    }

    private ResourceOpenHandleModel OpenResult(TestCompletedOperation operation)
        => new(Handle(operation.ResourcePath), operation.HandleId, operation.Mode, 0);
}

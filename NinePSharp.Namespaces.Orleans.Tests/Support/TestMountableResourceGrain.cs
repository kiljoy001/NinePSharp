using System.Security.Cryptography;
using NinePSharp.Constants;
using Orleans.Runtime;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

public interface ITestMountableResourceGrain : IGrainWithStringKey
{
    Task<TestResourceDiagnostics> GetDiagnosticsAsync();

    Task DeactivateAsync();

    Task DelayNextReadAsync(int milliseconds);
}

[GenerateSerializer]
public sealed record TestResourceDiagnostics(
    [property: Id(0)] int Mutations,
    [property: Id(1)] int Clunks,
    [property: Id(2)] string[] Children,
    [property: Id(3)] int Activations,
    [property: Id(4)] string RuntimeIdentity);

public sealed class TestMountableResourceGrain : Grain, IMountableResourceGrain, ITestMountableResourceGrain
{
    private readonly IPersistentState<TestResourceState> state;
    private int nextReadDelay;

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
            state.State.Nodes[1] = new TestResourceNode { Name = "/", Directory = true };
            state.State.Nodes[2] = new TestResourceNode { Name = "job" };
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

        byte[] data = RequireNode(openHandle.Resource.Identity.Path).Data;
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
        uint mode = node.Directory
            ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755
            : NinePConstants.Mode0644;
        return Task.FromResult(new ResourceStatModel(
            Handle(resource.Identity.Path),
            node.Name,
            mode,
            0,
            0,
            checked((ulong)node.Data.Length),
            "glenda",
            "glenda",
            "glenda"));
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
            throw new InvalidOperationException("file already exists");
        }

        ulong path = ++state.State.NextPath;
        state.State.Nodes.Add(path, new TestResourceNode { Name = name, Directory = directoryEntry });
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

    private static string OperationKey(ResourceOperationIdModel operation)
        => $"{operation.SessionId.Length}:{operation.SessionId}:{operation.Sequence}";

    private static string Fingerprint(params object[] values)
        => string.Join('\0', values.Select(value => value.ToString()));
}

[GenerateSerializer]
public sealed class TestResourceState
{
    [Id(0)]
    public bool Initialized { get; set; }

    [Id(1)]
    public ulong NextPath { get; set; }

    [Id(2)]
    public Dictionary<ulong, TestResourceNode> Nodes { get; set; } = new();

    [Id(3)]
    public Dictionary<string, TestCompletedOperation> Completed { get; set; } = new(StringComparer.Ordinal);

    [Id(4)]
    public int Mutations { get; set; }

    [Id(5)]
    public int Clunks { get; set; }

    [Id(6)]
    public int Activations { get; set; }
}

[GenerateSerializer]
public sealed class TestResourceNode
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public bool Directory { get; set; }

    [Id(2)]
    public uint Version { get; set; }

    [Id(3)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    [Id(4)]
    public Dictionary<string, ulong> Children { get; set; } = new(StringComparer.Ordinal);
}

[GenerateSerializer]
public sealed class TestCompletedOperation
{
    [Id(0)]
    public string Fingerprint { get; set; } = string.Empty;

    [Id(1)]
    public ulong ResourcePath { get; set; }

    [Id(2)]
    public string HandleId { get; set; } = string.Empty;

    [Id(3)]
    public byte Mode { get; set; }

    [Id(4)]
    public uint Count { get; set; }
}

internal sealed class TestMountableResourceResolver : IMountableResourceResolver
{
    private readonly IGrainFactory grainFactory;

    internal TestMountableResourceResolver(IGrainFactory grainFactory)
    {
        this.grainFactory = grainFactory;
    }

    public IMountableResourceGrain Resolve(ResourceIdentityModel identity)
        => grainFactory.GetGrain<IMountableResourceGrain>(identity.Device);
}

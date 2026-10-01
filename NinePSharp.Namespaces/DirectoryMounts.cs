namespace NinePSharp.Namespaces;

// Identity survives replacement and selected unmount, but not complete unmount.
internal sealed class DirectoryMountHead(IReadOnlyList<MountBinding> members)
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal IReadOnlyList<MountBinding> Members { get; set; } = members;

    internal async Task RetireAsync()
    {
        await Gate.WaitAsync();
        try { Members = Array.Empty<MountBinding>(); }
        finally { Gate.Release(); }
    }
}

/// <summary>Use an asynchronous mount operation when a directory stream owns the head.</summary>
public sealed class NamespaceMutationBusyException() : InvalidOperationException(
    "Directory IO retains this mount head; use the asynchronous mount operation.");

public sealed partial class MountTable
{
    private readonly Dictionary<ResourceIdentity, DirectoryMountHead> directoryHeads = new();

    internal DirectoryMountHead? RetainDirectoryHead(ResourceIdentity identity)
    {
        lock (gate)
        {
            EnsureOpen();
            if (!heads.TryGetValue(identity, out MountHead? head) || head.Mounts.Count < 2) return null;
            if (!directoryHeads.TryGetValue(identity, out DirectoryMountHead? retained))
                directoryHeads.Add(identity, retained = new DirectoryMountHead(head.Mounts));
            return retained;
        }
    }

    private IDisposable? EnterDirectoryMutation(ResourceIdentity identity, DirectoryMountHead? held)
    {
        if (!directoryHeads.TryGetValue(identity, out DirectoryMountHead? head) || head == held) return null;
        if (!head.Gate.Wait(0)) throw new NamespaceMutationBusyException();
        return new HeadRelease(head);
    }

    private void UpdateDirectoryHead(ResourceIdentity identity, IReadOnlyList<MountBinding>? members)
    {
        if (!directoryHeads.TryGetValue(identity, out DirectoryMountHead? head)) return;
        head.Members = members ?? Array.Empty<MountBinding>();
        if (members is null) directoryHeads.Remove(identity);
    }

    private async ValueTask<T> MutateDirectoryAsync<T>(ResourceIdentity identity,
        Func<DirectoryMountHead?, T> mutate, CancellationToken cancellationToken)
    {
        while (true)
        {
            DirectoryMountHead? head;
            lock (gate) { EnsureOpen(); directoryHeads.TryGetValue(identity, out head); }
            if (head is not null) await head.Gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    directoryHeads.TryGetValue(identity, out DirectoryMountHead? current);
                    if (head != current) continue;
                    return mutate(head);
                }
            }
            finally { head?.Gate.Release(); }
        }
    }

    /// <summary>Mounts a service, asynchronously waiting for retained directory IO.</summary>
    public ValueTask<MountBinding> MountAsync(ResourceHandle target, ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace, string? spec = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mountedOn);
        EnsureMountAllowed(target);
        if (!mountedOn.IsDirectory && flags.Order() == MountFlags.Replace)
            throw new NamespaceException(NamespaceError.MountTargetMustBeDirectory, "A service mount target must be a directory.");
        return MutateDirectoryAsync(mountedOn.Identity,
            head => Mount(target, mountedOn, flags, spec, null, head), cancellationToken);
    }

    /// <summary>Binds a channel, asynchronously waiting for retained directory IO.</summary>
    public ValueTask<MountBinding> MountAsync(NamespaceChannel source, ResourceHandle mountedOn,
        MountFlags flags = MountFlags.Replace, string? spec = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mountedOn);
        if ((flags & MountFlags.Cache) != 0)
            throw new NamespaceException(NamespaceError.InvalidMountFlags, "The cache flag is valid only for service mounts.");
        ChannelFrame frame = source.CurrentFrame;
        IReadOnlyList<MountBinding>? sourceMounts = frame.MountedFrom is null
            ? frame.Union : Find(frame.MountedFrom.Identity)?.Mounts;
        ResourceHandle target = sourceMounts is { Count: > 0 } ? sourceMounts[0].Target : frame.Handle;
        return MutateDirectoryAsync(mountedOn.Identity,
            head => Mount(target, mountedOn, flags, spec, sourceMounts, head), cancellationToken);
    }

    /// <summary>Unmounts members after an admitted union step releases its head lease.</summary>
    public async ValueTask UnmountAsync(ResourceHandle mountedOn, ResourceHandle? mounted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mountedOn);
        await MutateDirectoryAsync<object?>(mountedOn.Identity, head =>
        {
            UnmountCore(mountedOn, mounted, head);
            return null;
        }, cancellationToken);
    }

    private sealed class HeadRelease(DirectoryMountHead head) : IDisposable
    {
        public void Dispose() => head.Gate.Release();
    }
}

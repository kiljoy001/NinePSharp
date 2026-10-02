using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Implements path-based Plan 9 namespace syscalls for a virtual process.</summary>
public sealed class NamespaceSyscalls
{
    private readonly IResourceOperations resources;

    public NamespaceSyscalls(IResourceOperations resources)
        => this.resources = resources ?? throw new ArgumentNullException(nameof(resources));

    /// <summary>Binds a resource resolved from the process namespace onto a target path.</summary>
    public async ValueTask<MountBinding> BindAsync(
        VProcess process,
        string name,
        string old,
        MountFlags flags = MountFlags.Replace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        NamespaceChannel source = await ResolveAsync(process, name, crossFinalMount: true, cancellationToken: cancellationToken);
        NamespaceChannel target = await ResolveAsync(process, old, crossFinalMount: false, cancellationToken: cancellationToken);
        return await process.ProcessGroup.MountTable.MountAsync(source, target.Current, flags, cancellationToken: cancellationToken);
    }

    /// <summary>Mounts an authenticated read-write service descriptor onto a target path.</summary>
    public async ValueTask<MountBinding> MountAsync(
        VProcess process,
        NamespaceMountSource source,
        string old,
        MountFlags flags = MountFlags.Replace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(source);
        if ((source.Mode & 3) != NinePConstants.ORDWR)
        {
            throw new NamespaceException(
                NamespaceError.MountSourceNotReadWrite,
                "A service mount source must be open read-write.");
        }

        if (source.RequiresAuthentication && !source.Authenticated)
        {
            throw new NamespaceException(
                NamespaceError.MountAuthenticationRequired,
                "The service requires an authenticated mount source.");
        }

        NamespaceChannel target = await ResolveAsync(process, old, crossFinalMount: false, cancellationToken: cancellationToken);
        MountBinding binding = await process.ProcessGroup.MountTable.MountAsync(
            source.Root.Current,
            target.Current,
            flags,
            source.AttachName,
            cancellationToken);
        if (source.CloseAsync is not null)
        {
            await source.CloseAsync();
        }

        return binding;
    }

    /// <summary>Removes every mount or one selected member at a target path.</summary>
    public async ValueTask UnmountAsync(
        VProcess process,
        string old,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        NamespaceChannel target = await ResolveAsync(process, old, crossFinalMount: false, cancellationToken: cancellationToken);
        ResourceHandle? mounted = name is null
            ? null
            : (await ResolveAsync(process, name, crossFinalMount: true, cancellationToken: cancellationToken)).Current;
        await process.ProcessGroup.MountTable.UnmountAsync(target.Current, mounted, cancellationToken);
    }

    private async ValueTask<NamespaceChannel> ResolveAsync(
        VProcess process,
        string path,
        bool crossFinalMount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        NamespaceChannel channel = path.StartsWith("/", StringComparison.Ordinal)
            ? process.Root.Clone()
            : process.CurrentDirectory.Clone();

        // Like parsename, discard dot elements without collapsing dot-dot across
        // mount boundaries. A trailing dot must not turn Amount into Abind.
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => name != ".").ToArray();

        NamespaceWalkResult result = await new NamespaceNavigator(process.ProcessGroup.MountTable, resources)
            .WalkAsync(channel, names, crossFinalMount, cancellationToken);
        if (!result.Complete(names.Length))
        {
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The namespace path could not be resolved.");
        }

        if ((path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("/.", StringComparison.Ordinal))
            && !result.Channel.Current.IsDirectory)
        {
            throw new NamespaceException(NamespaceError.ResourceNotDirectory, "The namespace path is not a directory.");
        }

        return result.Channel;
    }
}

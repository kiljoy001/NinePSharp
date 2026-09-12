using System.Globalization;
using System.Text;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Virtual Plan 9 control files for inspecting and mutating process namespaces.</summary>
public sealed class NamespaceControlResource : IResourceDataOperations
{
    private const string Provider = "namespace-control";
    private readonly VProcessTable processes;
    private readonly NamespaceSyscalls syscalls;

    /// <summary>Initializes a control resource rooted at <c>/</c>.</summary>
    public NamespaceControlResource(VProcessTable processes, IResourceOperations resources)
    {
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
        syscalls = new NamespaceSyscalls(resources ?? throw new ArgumentNullException(nameof(resources)));
        Root = Handle("/");
    }

    /// <summary>Gets the root handle to attach to a 9P session.</summary>
    public ResourceHandle Root { get; }

    /// <inheritdoc/>
    public ValueTask<ResourceHandle?> WalkAsync(
        ResourceHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsDirectory(directory))
        {
            return ValueTask.FromResult<ResourceHandle?>(null);
        }

        string path = Join(PathOf(directory), name);
        return ValueTask.FromResult(IsKnown(path) ? Handle(path) : null);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathOf(directory);
        string[] children = Children(path);
        return ValueTask.FromResult<IReadOnlyList<ResourceDirectoryEntry>>(
            children.Select(name => new ResourceDirectoryEntry(name, Handle(Join(path, name)))).ToArray());
    }

    /// <inheritdoc/>
    public ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Namespace control files cannot be created.");

    /// <inheritdoc/>
    public ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsKnown(PathOf(resource)))
        {
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The control resource does not exist.");
        }

        return ValueTask.FromResult(new ResourceOpenHandle(resource, PathOf(resource), mode, 0));
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] content = Encoding.UTF8.GetBytes(ReadFile(PathOf(openHandle.Resource)));
        if (offset >= (ulong)content.Length)
        {
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        }

        int start = checked((int)offset);
        int length = Math.Min(checked((int)count), content.Length - start);
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(content.AsMemory(start, length));
    }

    /// <inheritdoc/>
    public async ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset != 0 || !PathOf(openHandle.Resource).EndsWith("/ctl", StringComparison.Ordinal))
        {
            throw new NamespaceException(NamespaceError.InvalidOperation, "Only ctl accepts writes at offset zero.");
        }

        string command = Encoding.UTF8.GetString(data.Span).Trim();
        await ExecuteAsync(PathOf(openHandle.Resource), command, cancellationToken);
        return checked((uint)data.Length);
    }

    /// <inheritdoc/>
    public ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathOf(resource);
        bool directory = IsDirectory(resource);
        string name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..];
        uint mode = directory
            ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755
            : NinePConstants.Mode0644;
        ulong length = directory ? 0 : checked((ulong)Encoding.UTF8.GetByteCount(ReadFile(path)));
        return ValueTask.FromResult(new ResourceStat(resource, name, mode, 0, 0, length, "system", "system", "system"));
    }

    /// <inheritdoc/>
    public ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Namespace control files cannot be created.");

    /// <inheritdoc/>
    public ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Namespace control files cannot be removed.");

    private async ValueTask ExecuteAsync(string path, string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new NamespaceException(NamespaceError.InvalidOperation, "The ctl command is empty.");
        }

        long processId = ParseProcessId(path);
        VProcess process = processes.Get(processId);
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0])
        {
            case "bind" when parts.Length is 3 or 4:
                await syscalls.BindAsync(process, parts[1], parts[2], ParseFlags(parts, 3), cancellationToken);
                return;
            case "mounts-disabled" when parts.Length == 2:
                process.ProcessGroup.MountTable.SetMountsDisabled(ParseBoolean(parts[1]));
                return;
            case "unmount" when parts.Length is 2 or 3:
                await syscalls.UnmountAsync(process, parts[1], parts.Length == 3 ? parts[2] : null, cancellationToken);
                return;
            case "rfork" when parts.Length is 2 or 3:
                processes.RforkNamespace(processId, ParseForkMode(parts[1]), parts.Length == 3 && parts[2] == "nomounts");
                return;
            default:
                throw new NamespaceException(NamespaceError.InvalidOperation, "The ctl command is not recognized.");
        }
    }

    private string ReadFile(string path)
    {
        long processId = ParseProcessId(path);
        VProcess process = processes.Get(processId);
        if (path.EndsWith("/status", StringComparison.Ordinal))
        {
            MountTable table = process.ProcessGroup.MountTable;
            return $"pid={process.Id}\nparent={(process.ParentId?.ToString(CultureInfo.InvariantCulture) ?? "none")}\n" +
                $"group={process.ProcessGroup.Id}\nmounts={table.Snapshot().MountHeads.Count}\n" +
                $"mounts-disabled={table.MountsDisabled.ToString().ToLowerInvariant()}\n";
        }

        if (path.EndsWith("/ns", StringComparison.Ordinal))
        {
            NamespaceSnapshot snapshot = process.ProcessGroup.MountTable.Snapshot();
            var builder = new StringBuilder();
            builder.Append("root=").Append(string.Join('/', process.Root.VisiblePath)).Append('\n');
            builder.Append("cwd=").Append(string.Join('/', process.CurrentDirectory.VisiblePath)).Append('\n');
            foreach (MountHead head in snapshot.MountHeads)
            {
                foreach (MountBinding binding in head.Mounts)
                {
                    builder.Append("mount=").Append(binding.MountId).Append(' ')
                        .Append(head.From.Identity.Provider).Append(':').Append(head.From.Identity.Device)
                        .Append(" -> ").Append(binding.Target.Identity.Provider).Append(':').Append(binding.Target.Identity.Device)
                        .Append(" flags=").Append(binding.Flags).Append('\n');
                }
            }

            return builder.ToString();
        }

        return string.Empty;
    }

    private string[] Children(string path)
    {
        if (path == "/") return new[] { "proc" };
        if (path == "/proc") return processes.Snapshot().Select(process => process.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (path.StartsWith("/proc/", StringComparison.Ordinal) && path.Count(character => character == '/') == 2)
            return new[] { "ns", "status", "ctl" };
        return Array.Empty<string>();
    }

    private bool IsKnown(string path)
        => path == "/" || path == "/proc" ||
            (path.StartsWith("/proc/", StringComparison.Ordinal) &&
             (path.Count(character => character == '/') == 2
                ? long.TryParse(path[6..], NumberStyles.None, CultureInfo.InvariantCulture, out long id) && processes.Snapshot().Any(process => process.Id == id)
                : (path.EndsWith("/ns", StringComparison.Ordinal) || path.EndsWith("/status", StringComparison.Ordinal) || path.EndsWith("/ctl", StringComparison.Ordinal)) && ProcessPathExists(path)));

    private bool ProcessPathExists(string path)
    {
        string value = path[6..path.IndexOf('/', 6)];
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && processes.Snapshot().Any(process => process.Id == id);
    }

    private bool IsDirectory(ResourceHandle handle)
        => PathOf(handle) is "/" or "/proc" || (PathOf(handle).StartsWith("/proc/", StringComparison.Ordinal) && PathOf(handle).Count(character => character == '/') == 2);

    private static string PathOf(ResourceHandle handle) => handle.Identity.Device;

    private static string Join(string parent, string child)
        => parent == "/" ? "/" + child : parent + "/" + child;

    private static ResourceHandle Handle(string path)
        => new(new ResourceIdentity(Provider, path, StablePath(path)), IsDirectoryPath(path) ? QidType.QTDIR : QidType.QTFILE);

    private static bool IsDirectoryPath(string path)
        => path is "/" or "/proc" || path.StartsWith("/proc/", StringComparison.Ordinal) && path.Count(character => character == '/') == 2;

    private static ulong StablePath(string path)
    {
        ulong hash = 1469598103934665603UL;
        foreach (char character in path)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static long ParseProcessId(string path)
    {
        string value = path[6..path.IndexOf('/', 6)];
        return long.Parse(value, CultureInfo.InvariantCulture);
    }

    private static MountFlags ParseFlags(string[] parts, int index)
        => parts.Length <= index ? MountFlags.Replace : parts[index] switch
        {
            "replace" => MountFlags.Replace,
            "before" => MountFlags.Before,
            "after" => MountFlags.After,
            _ => throw new NamespaceException(NamespaceError.InvalidMountFlags, "The bind order is invalid."),
        };

    private static NamespaceForkMode ParseForkMode(string value)
        => value switch
        {
            "share" => NamespaceForkMode.Share,
            "copy" => NamespaceForkMode.Copy,
            "empty" => NamespaceForkMode.Empty,
            _ => throw new NamespaceException(NamespaceError.InvalidOperation, "The rfork mode is invalid."),
        };

    private static bool ParseBoolean(string value)
        => value switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new NamespaceException(NamespaceError.InvalidOperation, "The boolean value is invalid."),
        };
}

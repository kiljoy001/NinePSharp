using System.Buffers.Binary;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Origin used by the Plan 9 seek syscall.</summary>
public enum Plan9SeekWhence
{
    /// <summary>Position relative to the start of the resource.</summary>
    Set,
    /// <summary>Position relative to the shared channel offset.</summary>
    Current,
    /// <summary>Position relative to the current file length.</summary>
    End,
}

/// <summary>Native open flags, including the descriptor-local OCEXEC flag.</summary>
public readonly record struct Plan9OpenRequest(byte Mode)
{
    /// <summary>Gets whether exec closes the newly allocated descriptor.</summary>
    public bool CloseOnExec => (Mode & NinePConstants.OCEXEC) != 0;

    /// <summary>Gets the mode passed to the provider, as in chan.c:Aopen.</summary>
    public byte ProviderMode => (byte)(Mode & ~NinePConstants.OCEXEC);

    internal void Validate()
    {
        const byte allowed = 3 | NinePConstants.OTRUNC | NinePConstants.OCEXEC | NinePConstants.ORCLOSE;
        if ((Mode & ~allowed) != 0) throw new NamespaceFidException("invalid open mode");
    }

    internal void ValidateResource(ResourceHandle resource)
    {
        if (resource.IsDirectory && (ProviderMode & ~NinePConstants.ORCLOSE) != NinePConstants.OREAD)
            throw new NamespaceFidException("directories may only be opened for reading");
    }
}

/// <summary>
/// File IO, bounded metadata and directory reads for one virtual process. Descriptor admission and
/// publication are fenced by process lifetime; provider awaits retain a channel
/// reference without holding process or descriptor-table locks.
/// </summary>
public sealed class Plan9FileSyscalls
{
    private readonly VProcess process;
    private readonly INamespaceDataPlane dataPlane;
    private readonly Func<ResourceOperationContext> contextFactory;
    private readonly DirectoryReadMode directoryReadMode;
    private readonly IDirectoryStatOperations? directoryStats;
    private readonly IFileStatOperations? fileStats;

    /// <summary>Initializes syscalls with authenticated, unique provider operation contexts.</summary>
    public Plan9FileSyscalls(VProcess process, INamespaceDataPlane dataPlane, Func<ResourceOperationContext> contextFactory,
        DirectoryReadMode directoryReadMode = DirectoryReadMode.Metadata, IDirectoryStatOperations? directoryStats = null,
        IFileStatOperations? fileStats = null)
    {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.dataPlane = dataPlane ?? throw new ArgumentNullException(nameof(dataPlane));
        this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        if (!Enum.IsDefined(directoryReadMode)) throw new ArgumentOutOfRangeException(nameof(directoryReadMode));
        this.directoryReadMode = directoryReadMode;
        this.directoryStats = directoryStats;
        this.fileStats = fileStats;
    }

    /// <summary>Resolves a process path and opens it in the lowest free descriptor.</summary>
    public async ValueTask<int> OpenAsync(string path, Plan9OpenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        request.Validate();
        NamespaceChannel start = path.StartsWith('/') ? process.Root.Clone() : process.CurrentDirectory.Clone();
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(name => name != ".").ToArray();
        NamespaceWalkResult result = await dataPlane.WalkAsync(start, names, cancellationToken);
        if (!result.Complete(names.Length))
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The namespace path could not be resolved.");
        if ((path.EndsWith('/') || path.EndsWith("/.", StringComparison.Ordinal)) && !result.Channel.Current.IsDirectory)
            throw new NamespaceException(NamespaceError.ResourceNotDirectory, "The namespace path is not a directory.");
        DirectoryMountHead? union = null;
        if (directoryReadMode == DirectoryReadMode.ProviderStream)
        {
            MountTable mounts = process.ProcessGroup.MountTable;
            ChannelFrame frame = result.Channel.CurrentFrame;
            ResourceHandle mountedOn = frame.MountedFrom ?? frame.Handle;
            MountHead? head = mounts.Find(mountedOn.Identity);
            union = mounts.RetainDirectoryHead(mountedOn.Identity);
            if (head is not null)
                result.Channel.ReplaceCurrent(new ChannelFrame(frame.Name, head.Mounts[0].Target, mountedOn, head.Mounts));
        }
        request.ValidateResource(result.Channel.Current);

        // Allocate both identities before opening: even a failing context factory
        // cannot strand a provider handle which has already been returned.
        ResourceOperationContext openContext = contextFactory();
        ResourceOperationContext closeContext = contextFactory();
        ResourceOpenHandle opened = await dataPlane.OpenAsync(result.Channel, request.ProviderMode, openContext, cancellationToken);
        return await PublishAsync(opened, request.CloseOnExec, closeContext, result.Channel, union);
    }

    /// <summary>
    /// Creates a missing name or opens an existing name with OTRUNC. OEXCL rejects
    /// an existing name and never performs a truncating fallback after provider create.
    /// </summary>
    public async ValueTask<int> CreateAsync(string path, Plan9CreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Plan9OpenRequest open = request.OpenRequest;
        if ((path.EndsWith('/') || path.EndsWith("/.", StringComparison.Ordinal)) && !request.IsDirectory)
            throw new NamespaceFidException("create without DMDIR");
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(name => name != ".").ToArray();
        if (names.Length == 0)
            throw new NamespaceException(NamespaceError.ResourceAlreadyExists, "file already exists");
        NamespaceChannel start = path.StartsWith('/') ? process.Root.Clone() : process.CurrentDirectory.Clone();
        NamespaceWalkResult parent = await dataPlane.WalkAsync(start, names[..^1], cancellationToken);
        if (!parent.Complete(names.Length - 1))
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The create parent could not be resolved.");
        if (!parent.Channel.Current.IsDirectory)
            throw new NamespaceException(NamespaceError.ResourceNotDirectory, "The create parent is not a directory.");

        // Reserve cleanup identity before any operation can return an owned handle.
        ResourceOperationContext closeContext = contextFactory();
        ResourceOpenHandle opened = await CreateOrTruncateAsync(parent.Channel, names[^1], request, open, cancellationToken);
        NamespaceChannel createdChannel = parent.Channel.Clone();
        createdChannel.Push(new ChannelFrame(names[^1], opened.Resource));
        return await PublishAsync(opened, open.CloseOnExec, closeContext, createdChannel);
    }

    private async ValueTask<ResourceOpenHandle> CreateOrTruncateAsync(
        NamespaceChannel parent, string name, Plan9CreateRequest request, Plan9OpenRequest open, CancellationToken cancellationToken)
    {
        NamespaceWalkResult existing = await dataPlane.WalkAsync(parent, new[] { name }, cancellationToken);
        if (existing.Complete(1))
        {
            if (request.Exclusive)
                throw new NamespaceException(NamespaceError.ResourceAlreadyExists, "file already exists");
            return await OpenTruncatedAsync(existing.Channel, open, cancellationToken);
        }

        try
        {
            if (request.IsDirectory && (open.ProviderMode & ~NinePConstants.ORCLOSE) != NinePConstants.OREAD)
                throw new NamespaceFidException("directories may only be created for reading");
            NamespaceCreateResult created = await dataPlane.CreateAndOpenAsync(
                parent, name, request.Permissions, open.ProviderMode, contextFactory(), cancellationToken);
            return created.OpenHandle;
        }
        catch (Exception error) when (!request.Exclusive &&
            (error is ResourceCreateRejectedException || error is NamespaceException { Error: NamespaceError.CreateNotPermitted }))
        {
            // chan.c:Acreate retries the walk after an acknowledged create error.
            // A transport timeout/cancellation is not an acknowledged rejection.
            NamespaceWalkResult raced = await dataPlane.WalkAsync(parent, new[] { name }, cancellationToken);
            if (!raced.Complete(1)) throw;
            return await OpenTruncatedAsync(raced.Channel, open, cancellationToken);
        }
    }

    private ValueTask<ResourceOpenHandle> OpenTruncatedAsync(NamespaceChannel channel, Plan9OpenRequest request, CancellationToken cancellationToken)
    {
        var truncate = new Plan9OpenRequest((byte)(request.ProviderMode | NinePConstants.OTRUNC));
        truncate.ValidateResource(channel.Current);
        return dataPlane.OpenAsync(channel, truncate.ProviderMode, contextFactory(), cancellationToken);
    }

    private async ValueTask<int> PublishAsync(ResourceOpenHandle opened, bool closeOnExec, ResourceOperationContext closeContext,
        NamespaceChannel channel, DirectoryMountHead? union = null)
    {
        ValueTask Close() => dataPlane.ClunkAsync(opened, closeContext, CancellationToken.None);
        try
        {
            NamespaceChannel retained = channel.Clone();
            retained.UpdateCurrent(opened.Resource);
            return process.InstallDescriptor(opened, Close, closeOnExec,
                directoryReadMode == DirectoryReadMode.ProviderStream
                    ? new ProviderDirectoryCursor(dataPlane, opened, union, contextFactory, directoryStats)
                    : new DirectoryCursor(token => dataPlane.ReadDirectoryAsync(retained, token)), VisibleName(retained),
                retained.CurrentFrame.MountedFrom is not null);
        }
        catch
        {
            // sysopen/syscreate close on newfd failure. ORCLOSE is provider-owned.
            try { await Close(); } catch { }
            throw;
        }
    }

    /// <summary>Returns a bounded native stat record for a process path, or a two-byte size hint.</summary>
    public async ValueTask<ReadOnlyMemory<byte>> StatAsync(string path, uint count, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var (start, mounts) = process.CapturePath(path.StartsWith('/'));
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(name => name != ".").ToArray();
        NamespaceWalkResult result = await dataPlane.WalkAsync(start, names, cancellationToken);
        if (!result.Complete(names.Length))
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The namespace path could not be resolved.");
        ChannelFrame frame = result.Channel.CurrentFrame;
        ResourceHandle mountedOn = frame.MountedFrom ?? frame.Handle;
        MountHead? head = mounts.Find(mountedOn.Identity);
        if (head is not null)
            result.Channel.ReplaceCurrent(new ChannelFrame(frame.Name, head.Mounts[0].Target, mountedOn, head.Mounts));
        if ((path.EndsWith('/') || path.EndsWith("/.", StringComparison.Ordinal)) && !result.Channel.Current.IsDirectory)
            throw new NamespaceException(NamespaceError.ResourceNotDirectory, "The namespace path is not a directory.");
        cancellationToken.ThrowIfCancellationRequested();
        FileStatOperations.ValidateCount(count);
        // Once dispatched, the provider owns completion and temporary-fid cleanup.
        // Caller cancellation must not abandon that ownership.
        ReadOnlyMemory<byte> reply = await RequireFileStats().StatAsync(result.Channel.Current, count, CancellationToken.None);
        return FileStatRecords.Rewrite(reply, count, VisibleName(result.Channel));
    }

    /// <summary>Stats a retained descriptor without changing its position, cursor, or open ownership.</summary>
    public async ValueTask<ReadOnlyMemory<byte>> FStatAsync(int descriptor, uint count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using DescriptorLease lease = process.AcquireDescriptor(descriptor);
        FileStatOperations.ValidateCount(count);
        ReadOnlyMemory<byte> reply = await RequireFileStats().StatAsync(lease.Handle, count, CancellationToken.None);
        return FileStatRecords.Rewrite(reply, count, lease.VisibleName);
    }

    /// <summary>Applies one raw native stat update to the resource selected by a process path.</summary>
    public async ValueTask<uint> WStatAsync(string path, ReadOnlyMemory<byte> stat,
        CancellationToken cancellationToken = default)
    {
        byte[] owned = FileStatRecords.ValidateAndCopyUpdate(stat);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var (start, mounts) = process.CapturePath(path.StartsWith('/'));
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(name => name != ".").ToArray();
        NamespaceWalkResult result = await dataPlane.WalkAsync(start, names, cancellationToken);
        if (!result.Complete(names.Length))
            throw new NamespaceException(NamespaceError.ResourceNotFound, "The namespace path could not be resolved.");
        ChannelFrame frame = result.Channel.CurrentFrame;
        ResourceHandle mountedOn = frame.MountedFrom ?? frame.Handle;
        MountHead? head = mounts.Find(mountedOn.Identity);
        if (head is not null)
            result.Channel.ReplaceCurrent(new ChannelFrame(frame.Name, head.Mounts[0].Target, mountedOn, head.Mounts));
        if ((path.EndsWith('/') || path.EndsWith("/.", StringComparison.Ordinal)) && !result.Channel.Current.IsDirectory)
            throw new NamespaceException(NamespaceError.ResourceNotDirectory, "The namespace path is not a directory.");
        RejectMountPointRename(result.Channel.CurrentFrame.MountedFrom is not null, owned);
        cancellationToken.ThrowIfCancellationRequested();
        ResourceOperationContext context = contextFactory();
        return await RequireFileStats().WStatAsync(result.Channel.Current, owned, context, CancellationToken.None);
    }

    /// <summary>Applies one raw native stat update through a retained descriptor.</summary>
    public async ValueTask<uint> FWStatAsync(int descriptor, ReadOnlyMemory<byte> stat,
        CancellationToken cancellationToken = default)
    {
        byte[] owned = FileStatRecords.ValidateAndCopyUpdate(stat);
        cancellationToken.ThrowIfCancellationRequested();
        await using DescriptorLease lease = process.AcquireDescriptor(descriptor);
        if (lease.Handle.IsMountTransport) throw new NamespaceFidException("bad use of file descriptor");
        RejectMountPointRename(lease.IsMountPoint, owned);
        ResourceOperationContext context = contextFactory();
        return await RequireFileStats().WStatAsync(lease.Handle, owned, context, CancellationToken.None);
    }

    private IFileStatOperations RequireFileStats()
        => fileStats ?? throw new NotSupportedException("Configure native file stat operations for this syscall host.");

    private static string VisibleName(NamespaceChannel channel)
        => channel.Frames.Count == 1 ? string.Empty : channel.CurrentFrame.Name;

    private static void RejectMountPointRename(bool isMountPoint, ReadOnlySpan<byte> stat)
    {
        if (isMountPoint && BinaryPrimitives.ReadUInt16LittleEndian(stat[41..]) != 0)
            throw new NamespaceFidException("cannot rename mount point");
    }

    /// <summary>Reads at the shared channel offset and advances by the actual byte count.</summary>
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(int descriptor, uint count, CancellationToken cancellationToken = default)
        => PReadAsync(descriptor, -1, count, cancellationToken);

    /// <summary>Reads at an explicit offset; -1 selects native implicit-position semantics.</summary>
    public async ValueTask<ReadOnlyMemory<byte>> PReadAsync(int descriptor, long offset, uint count, CancellationToken cancellationToken = default)
    {
        await using DescriptorLease lease = process.AcquireDescriptor(descriptor, out MountTable callingNamespace);
        ValidateAccess(lease.Handle, write: false);
        if (lease.Handle.Resource.IsDirectory)
            return await lease.Directory.ReadAsync(offset, count, cancellationToken, callingNamespace);
        long position = offset == -1 ? lease.Offset : offset;
        if (position < 0) throw new NamespaceFidException("negative offset");
        ReadOnlyMemory<byte> result = await dataPlane.ReadAsync(lease.Handle, (ulong)position, count, cancellationToken);
        if ((uint)result.Length > count) throw new IOException("provider returned more bytes than requested");
        if (offset == -1) lease.AdvanceRead(result.Length);
        return result;
    }

    /// <summary>Reserves a shared range before writing and corrects short or failed completion.</summary>
    public ValueTask<uint> WriteAsync(int descriptor, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => PWriteAsync(descriptor, -1, data, cancellationToken);

    /// <summary>Writes at an explicit offset; -1 selects native implicit-position semantics.</summary>
    public async ValueTask<uint> PWriteAsync(int descriptor, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await using DescriptorLease lease = process.AcquireDescriptor(descriptor);
        ValidateAccess(lease.Handle, write: true);
        ResourceOperationContext context = contextFactory();
        long position = offset == -1 ? lease.ReserveWrite(data.Length) : offset;
        uint written;
        try
        {
            if (position < 0) throw new NamespaceFidException("negative offset");
            written = await dataPlane.WriteAsync(lease.Handle, (ulong)position, data, context, cancellationToken);
            if (written > data.Length) throw new IOException("provider returned more bytes than supplied");
        }
        catch
        {
            if (offset == -1) lease.CorrectWrite(data.Length);
            throw;
        }

        if (offset == -1) lease.CorrectWrite(data.Length - written);
        return written;
    }

    /// <summary>Changes the shared position; directory seek permits only absolute zero.</summary>
    public async ValueTask<long> SeekAsync(int descriptor, long offset, Plan9SeekWhence whence, CancellationToken cancellationToken = default)
    {
        await using DescriptorLease lease = process.AcquireDescriptor(descriptor);
        if (lease.Handle.Resource.IsDirectory && (whence != Plan9SeekWhence.Set || offset != 0))
            throw new NamespaceFidException("directory seek is only valid at offset zero");
        if (lease.Handle.Resource.IsDirectory)
        {
            await lease.Directory.RewindAsync(cancellationToken);
        }

        switch (whence)
        {
            case Plan9SeekWhence.Set:
                return lease.Seek(offset, relative: false);
            case Plan9SeekWhence.Current:
                return lease.Seek(offset, relative: true);
            case Plan9SeekWhence.End:
                ResourceStat stat = await dataPlane.StatAsync(lease.Handle, cancellationToken);
                return lease.Seek(checked((long)stat.Length + offset), relative: false);
            default:
                throw new ArgumentOutOfRangeException(nameof(whence));
        }
    }

    private static void ValidateAccess(ResourceOpenHandle handle, bool write)
    {
        if (handle.Resource.IsDirectory && write)
            throw new NamespaceFidException("directories cannot be written");
        byte access = (byte)(handle.Mode & 3);
        bool allowed = write
            ? access is NinePConstants.OWRITE or NinePConstants.ORDWR
            : access is NinePConstants.OREAD or NinePConstants.ORDWR or NinePConstants.OEXEC;
        if (!allowed) throw new NamespaceFidException("descriptor is not open for the requested access");
    }
}

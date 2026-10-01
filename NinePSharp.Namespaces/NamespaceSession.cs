using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Thrown when a 9P fid operation violates protocol state.</summary>
public sealed class NamespaceFidException : InvalidOperationException
{
    /// <summary>Initializes a fid error.</summary>
    public NamespaceFidException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Owns the ephemeral fid table for one 9P connection. Closing the session invalidates every fid.
/// </summary>
public sealed class NamespaceSession : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<uint, FidState> fids = new();
    private readonly Dictionary<uint, SemaphoreSlim> fidGates = new();
    private readonly INamespaceDataPlane dataPlane;
    private long operationSequence;
    private bool closed;

    /// <summary>Initializes a connection-local namespace session.</summary>
    public NamespaceSession(string sessionId, long processId, string user, INamespaceDataPlane dataPlane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        SessionId = sessionId;
        ProcessId = processId;
        User = user;
        this.dataPlane = dataPlane ?? throw new ArgumentNullException(nameof(dataPlane));
    }

    /// <summary>Gets the connection epoch used in mutation identities.</summary>
    public string SessionId { get; }

    /// <summary>Gets the associated virtual process.</summary>
    public long ProcessId { get; }

    /// <summary>Gets the authenticated user.</summary>
    public string User { get; }

    /// <summary>Attaches a new, unopened fid.</summary>
    public async ValueTask<ResourceHandle> AttachAsync(
        uint fid,
        ResourceHandle root,
        CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        EnsureFidAbsent(fid);
        NamespaceChannel channel = await dataPlane.AttachAsync(root, cancellationToken);
        lock (gate)
        {
            EnsureOpen();
            // AcquireAsync owns this fid until publication completes.
            fids.Add(fid, new FidState(channel));
        }

        return channel.Current;
    }

    /// <summary>Walks or clones an unopened fid, retaining no new fid after a partial walk.</summary>
    public async ValueTask<NamespaceWalkResult> WalkAsync(
        uint fid,
        uint newFid,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        await using FidLockSet locks = await AcquireAsync(fid, newFid, cancellationToken);
        FidState source = GetFid(fid);
        if (source.OpenHandle is not null)
        {
            throw new NamespaceFidException("cannot clone open fid");
        }

        if (names.Count > 0 && !source.Channel.Current.IsDirectory)
        {
            throw new NamespaceFidException("walk in non-directory");
        }

        if (fid != newFid)
        {
            EnsureFidAbsent(newFid);
        }

        NamespaceWalkResult result = await dataPlane.WalkAsync(source.Channel, names, cancellationToken);
        if (result.Complete(names.Count))
        {
            lock (gate)
            {
                EnsureOpen();
                // Both fid locks remain held across the provider walk.
                fids[newFid] = new FidState(result.Channel);
            }
        }

        return result;
    }

    /// <summary>Opens an existing fid once.</summary>
    public async ValueTask<ResourceOpenHandle> OpenAsync(
        uint fid,
        byte mode,
        CancellationToken cancellationToken = default)
        => await OpenAsync(fid, mode, requireDirectory: false, cancellationToken);

    /// <summary>Opens a fid and optionally enforces directory-only open semantics.</summary>
    public async ValueTask<ResourceOpenHandle> OpenAsync(
        uint fid,
        byte mode,
        bool requireDirectory,
        CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        FidState state = GetFid(fid);
        EnsureUnopened(state);
        ValidateOpenMode(state.Channel.Current, mode);
        if (requireDirectory && !state.Channel.Current.IsDirectory)
        {
            throw new NamespaceFidException("fid is not a directory");
        }

        ResourceOpenHandle opened = await dataPlane.OpenAsync(
            state.Channel,
            mode,
            NextContext(),
            cancellationToken);
        state.Channel.UpdateCurrent(opened.Resource);
        state.OpenHandle = opened;
        return opened;
    }

    /// <summary>Reads an open fid.</summary>
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        uint fid,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        ResourceOpenHandle openHandle = RequireOpen(GetFid(fid));
        ValidateReadable(openHandle.Mode);
        return await dataPlane.ReadAsync(openHandle, offset, count, cancellationToken);
    }

    /// <summary>Reads the visible entries of an open directory fid.</summary>
    public async ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(
        uint fid,
        CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        FidState state = GetFid(fid);
        ResourceOpenHandle openHandle = RequireOpen(state);
        ValidateReadable(openHandle.Mode);
        if (!state.Channel.Current.IsDirectory)
        {
            throw new NamespaceFidException("fid is not a directory");
        }

        return await dataPlane.ReadDirectoryAsync(state.Channel, cancellationToken);
    }

    /// <summary>Writes an open fid.</summary>
    public async ValueTask<uint> WriteAsync(
        uint fid,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        ResourceOpenHandle openHandle = RequireOpen(GetFid(fid));
        ValidateWritable(openHandle.Mode);
        return await dataPlane.WriteAsync(
            openHandle,
            offset,
            data,
            NextContext(),
            cancellationToken);
    }

    /// <summary>Reads metadata from either an open or unopened fid.</summary>
    public async ValueTask<ResourceStat> StatAsync(uint fid, CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        return await dataPlane.StatAsync(GetFid(fid).Channel, cancellationToken);
    }

    /// <summary>Creates and opens a child, replacing the directory selected by the fid.</summary>
    public async ValueTask<ResourceOpenHandle> CreateAsync(
        uint fid,
        string name,
        uint permissions,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.Contains('/', StringComparison.Ordinal))
        {
            throw new NamespaceFidException("invalid create name");
        }

        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        FidState state = GetFid(fid);
        EnsureUnopened(state);
        if (!state.Channel.Current.IsDirectory)
        {
            throw new NamespaceFidException("create in non-directory");
        }

        ValidateOpenFlags(mode);
        if ((permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            ValidateDirectoryOpenMode(mode);
        }

        NamespaceCreateResult result = await dataPlane.CreateAndOpenAsync(
            state.Channel,
            name,
            permissions,
            mode,
            NextContext(),
            cancellationToken);
        state.Channel = result.Channel;
        state.OpenHandle = result.OpenHandle;
        return result.OpenHandle;
    }

    /// <summary>Clunks a fid. The fid stays invalid even when provider cleanup fails.</summary>
    public async ValueTask ClunkAsync(uint fid, CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        FidState state = RemoveFid(fid);
        if (state.OpenHandle is not null)
        {
            await dataPlane.ClunkAsync(state.OpenHandle, NextContext(), cancellationToken);
        }
    }

    /// <summary>Removes a resource and always invalidates its fid.</summary>
    public async ValueTask RemoveAsync(uint fid, CancellationToken cancellationToken = default)
    {
        await using FidLockSet locks = await AcquireAsync(fid, cancellationToken);
        FidState state = RemoveFid(fid);
        await dataPlane.RemoveAsync(state.Channel, state.OpenHandle, NextContext(), cancellationToken);
    }

    /// <summary>Reports whether a fid is currently allocated. Intended for hosts and diagnostics.</summary>
    public bool ContainsFid(uint fid)
    {
        lock (gate)
        {
            return fids.ContainsKey(fid);
        }
    }

    /// <summary>Returns the resource currently selected by a fid.</summary>
    public ResourceHandle GetFidResource(uint fid) => GetFid(fid).Channel.Current;

    /// <summary>Ends the connection and best-effort clunks all open fids.</summary>
    public async ValueTask DisposeAsync()
    {
        SemaphoreSlim[] semaphores;
        FidState[] states;
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            semaphores = fidGates.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        }

        foreach (SemaphoreSlim semaphore in semaphores)
        {
            await semaphore.WaitAsync();
        }

        lock (gate)
        {
            states = fids.Values.ToArray();
            fids.Clear();
        }

        foreach (FidState state in states)
        {
            if (state.OpenHandle is null)
            {
                continue;
            }

            try
            {
                await dataPlane.ClunkAsync(state.OpenHandle, NextContext(), CancellationToken.None);
            }
            catch
            {
                // A broken connection must not retain the remaining fids.
            }
        }

        for (int index = semaphores.Length - 1; index >= 0; index--)
        {
            semaphores[index].Release();
        }

        lock (gate)
        {
            foreach (SemaphoreSlim semaphore in fidGates.Values)
            {
                semaphore.Dispose();
            }

            fidGates.Clear();
        }
    }

    private static void ValidateOpenMode(ResourceHandle resource, byte mode)
    {
        ValidateOpenFlags(mode);

        if (resource.IsDirectory)
        {
            ValidateDirectoryOpenMode(mode);
        }
    }

    private static void ValidateOpenFlags(byte mode)
    {
        const byte allowedFlags = 3 | NinePConstants.OTRUNC | NinePConstants.ORCLOSE;
        if ((mode & ~allowedFlags) != 0)
        {
            throw new NamespaceFidException("invalid open mode");
        }
    }

    private static void ValidateDirectoryOpenMode(byte mode)
    {
        if ((mode & ~NinePConstants.ORCLOSE) != NinePConstants.OREAD)
        {
            throw new NamespaceFidException("directories may only be opened for reading");
        }
    }

    private static void ValidateReadable(byte mode)
    {
        byte access = (byte)(mode & 3);
        if (access is not NinePConstants.OREAD and not NinePConstants.ORDWR and not NinePConstants.OEXEC)
        {
            throw new NamespaceFidException("fid is not open for reading");
        }
    }

    private static void ValidateWritable(byte mode)
    {
        byte access = (byte)(mode & 3);
        if (access is not NinePConstants.OWRITE and not NinePConstants.ORDWR)
        {
            throw new NamespaceFidException("fid is not open for writing");
        }
    }

    private static void EnsureUnopened(FidState state)
    {
        if (state.OpenHandle is not null)
        {
            throw new NamespaceFidException("fid is already open");
        }
    }

    private static ResourceOpenHandle RequireOpen(FidState state)
        => state.OpenHandle ?? throw new NamespaceFidException("fid is not open");

    private ResourceOperationContext NextContext()
    {
        ulong sequence = checked((ulong)Interlocked.Increment(ref operationSequence));
        return new ResourceOperationContext(new ResourceOperationId(SessionId, sequence), ProcessId, User);
    }

    private FidState GetFid(uint fid)
    {
        lock (gate)
        {
            EnsureOpen();
            return fids.TryGetValue(fid, out FidState? state)
                ? state
                : throw new NamespaceFidException("unknown fid");
        }
    }

    private FidState RemoveFid(uint fid)
    {
        lock (gate)
        {
            EnsureOpen();
            if (!fids.Remove(fid, out FidState? state))
            {
                throw new NamespaceFidException("unknown fid");
            }

            return state;
        }
    }

    private void EnsureFidAbsent(uint fid)
    {
        lock (gate)
        {
            if (fids.ContainsKey(fid))
            {
                throw new NamespaceFidException("duplicate fid");
            }
        }
    }

    private void EnsureOpen()
    {
        lock (gate)
        {
            if (closed)
            {
                throw new ObjectDisposedException(nameof(NamespaceSession));
            }
        }
    }

    private SemaphoreSlim GetFidGate(uint fid)
    {
        lock (gate)
        {
            EnsureOpen();
            if (!fidGates.TryGetValue(fid, out SemaphoreSlim? semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                fidGates.Add(fid, semaphore);
            }

            return semaphore;
        }
    }

    private ValueTask<FidLockSet> AcquireAsync(uint fid, CancellationToken cancellationToken)
        => AcquireCoreAsync(new[] { fid }, cancellationToken);

    private ValueTask<FidLockSet> AcquireAsync(uint first, uint second, CancellationToken cancellationToken)
        => AcquireCoreAsync(new[] { first, second }, cancellationToken);

    private async ValueTask<FidLockSet> AcquireCoreAsync(
        IEnumerable<uint> fidsToLock,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim[] semaphores = fidsToLock.Distinct().Order().Select(GetFidGate).ToArray();
        var acquired = new List<SemaphoreSlim>(semaphores.Length);
        try
        {
            foreach (SemaphoreSlim semaphore in semaphores)
            {
                await semaphore.WaitAsync(cancellationToken);
                acquired.Add(semaphore);
            }

            return new FidLockSet(acquired);
        }
        catch
        {
            for (int index = acquired.Count - 1; index >= 0; index--)
            {
                acquired[index].Release();
            }

            throw;
        }
    }

    private sealed class FidState
    {
        internal FidState(NamespaceChannel channel)
        {
            Channel = channel;
        }

        internal NamespaceChannel Channel { get; set; }

        internal ResourceOpenHandle? OpenHandle { get; set; }
    }

    private sealed class FidLockSet : IAsyncDisposable
    {
        private readonly IReadOnlyList<SemaphoreSlim> semaphores;

        internal FidLockSet(IReadOnlyList<SemaphoreSlim> semaphores)
        {
            this.semaphores = semaphores;
        }

        public ValueTask DisposeAsync()
        {
            for (int index = semaphores.Count - 1; index >= 0; index--)
            {
                semaphores[index].Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

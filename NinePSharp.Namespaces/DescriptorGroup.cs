namespace NinePSharp.Namespaces;

/// <summary>Controls descriptor-table inheritance independently of namespace inheritance.</summary>
public enum DescriptorForkMode
{
    /// <summary>Share the same table, as rfork without RFFDG or RFCFDG.</summary>
    Share,
    /// <summary>Copy slots while retaining the same open channels, as RFFDG.</summary>
    Copy,
    /// <summary>Start with an empty table, as RFCFDG.</summary>
    Empty,
}

/// <summary>A snapshot of a descriptor slot, not an additional channel reference.</summary>
public sealed record DescriptorSlot(int Number, ResourceOpenHandle Handle, bool CloseOnExec);

/// <summary>An admitted operation's reference to an open channel.</summary>
public sealed class DescriptorLease : IAsyncDisposable
{
    private readonly OpenDescriptorChannel channel;
    private int disposed;

    internal DescriptorLease(OpenDescriptorChannel channel) => this.channel = channel;

    /// <summary>Gets the provider handle retained by this operation.</summary>
    public ResourceOpenHandle Handle => channel.Handle;

    internal string? VisibleName => channel.VisibleName;

    /// <summary>Gets the mount-point state retained when this channel was opened.</summary>
    internal bool IsMountPoint => channel.IsMountPoint;

    /// <summary>Gets the shared implicit I/O offset for this open channel.</summary>
    internal long Offset => channel.Offset;

    internal long ReserveWrite(int length) => channel.ReserveWrite(length);

    internal void CorrectWrite(long unused) => channel.CorrectWrite(unused);

    internal void AdvanceRead(int actual) => channel.AdvanceRead(actual);

    internal long Seek(long offset, bool relative) => channel.Seek(offset, relative);

    internal IDirectoryCursor Directory => channel.Directory
        ?? throw new NamespaceFidException("descriptor has no directory cursor");

    /// <summary>Releases this operation's reference once, even if disposal is repeated.</summary>
    public ValueTask DisposeAsync()
        => Interlocked.Exchange(ref disposed, 1) == 0 ? channel.ReleaseAsync() : ValueTask.CompletedTask;
}

internal sealed class OpenDescriptorChannel
{
    private readonly Func<ValueTask> close;
    private readonly object positionGate = new();
    private int references = 1;
    private long offset;

    internal OpenDescriptorChannel(ResourceOpenHandle handle, Func<ValueTask> close,
        IDirectoryCursor? directory, string? visibleName = null, bool isMountPoint = false)
    {
        Handle = handle;
        this.close = close;
        Directory = handle.Resource.IsDirectory ? directory : null;
        VisibleName = visibleName;
        IsMountPoint = isMountPoint;
    }

    internal ResourceOpenHandle Handle { get; }
    internal IDirectoryCursor? Directory { get; }
    internal string? VisibleName { get; }
    internal bool IsMountPoint { get; }

    internal long Offset
    {
        get
        {
            lock (positionGate) return offset;
        }
    }

    internal long ReserveWrite(int length)
    {
        lock (positionGate)
        {
            long start = offset;
            offset = checked(offset + length);
            return start;
        }
    }

    internal void CorrectWrite(long unused)
    {
        lock (positionGate)
        {
            // sysfile.c subtracts from the current offset, even after a racing seek.
            offset = checked(offset - unused);
        }
    }

    internal void AdvanceRead(int actual)
    {
        lock (positionGate) offset = checked(offset + actual);
    }

    internal long Seek(long value, bool relative)
    {
        lock (positionGate)
        {
            long next = relative ? checked(offset + value) : value;
            if (next < 0) throw new NamespaceFidException("negative offset");
            offset = next;
            return next;
        }
    }

    // Retain is called only while an existing slot is locked and still owns a reference.
    internal void Retain() => Interlocked.Increment(ref references);

    internal async ValueTask ReleaseAsync()
    {
        if (Interlocked.Decrement(ref references) != 0) return;
        // Never invoke provider code inline under a process/table ownership lock.
        await Task.Yield();
        try
        {
            await close();
        }
        catch (Exception)
        {
            // 9front chan.c:cclose releases the Chan even when device close fails.
            // Durable provider cleanup/error accounting belongs to the Orleans adapter.
        }
        if (Directory is not null) await Directory.CloseAsync();
    }
}

/// <summary>A local Fgrp: shared descriptor slots own references to open channels.</summary>
public sealed class DescriptorGroup
{
    private readonly object gate = new();
    private readonly SortedDictionary<int, Entry> slots = new();
    private int capacity = 20;
    private int owners;
    private bool closed;

    /// <summary>Gets the number of processes sharing this table.</summary>
    public int OwnerCount { get { lock (gate) return owners; } }

    /// <summary>Gets whether the last process has released this table.</summary>
    public bool IsClosed { get { lock (gate) return closed; } }

    /// <summary>Gets a stable snapshot of current slots without acquiring channel ownership.</summary>
    public IReadOnlyList<DescriptorSlot> Snapshot()
    {
        lock (gate)
            return slots.Select(pair => new DescriptorSlot(pair.Key, pair.Value.Channel.Handle, pair.Value.CloseOnExec)).ToArray();
    }

    /// <summary>
    /// Installs a newly opened provider instance in the lowest free slot. Ownership
    /// transfers only on success; the caller must close the instance on failure.
    /// Use Duplicate to share an existing instance instead of installing it twice.
    /// Directory descriptors used by Plan9FileSyscalls need a readDirectoryAsync
    /// loader; its cursor is shared by duplicates and copied descriptor tables.
    /// An optional visibleName is retained for native fstat rewriting; null preserves
    /// the provider name and an empty string represents the visible root path.
    /// </summary>
    public int Install(ResourceOpenHandle handle, Func<ValueTask> closeAsync, bool closeOnExec = false,
        Func<CancellationToken, ValueTask<IReadOnlyList<ResourceStat>>>? readDirectoryAsync = null,
        string? visibleName = null, bool isMountPoint = false)
        => InstallWithCursor(handle, closeAsync, closeOnExec,
            readDirectoryAsync is null ? null : new DirectoryCursor(readDirectoryAsync), visibleName, isMountPoint);

    internal int InstallWithCursor(ResourceOpenHandle handle, Func<ValueTask> closeAsync, bool closeOnExec,
        IDirectoryCursor? directory, string? visibleName = null, bool isMountPoint = false)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(closeAsync);
        lock (gate)
        {
            EnsureOpen();
            int fd = FindFree();
            Grow(fd);
            slots.Add(fd, new Entry(new OpenDescriptorChannel(handle, closeAsync, directory, visibleName, isMountPoint), closeOnExec));
            return fd;
        }
    }

    /// <summary>Acquires an I/O reference that remains valid after its slot is closed.</summary>
    public DescriptorLease Acquire(int fd)
    {
        lock (gate)
        {
            Entry entry = Get(fd);
            entry.Channel.Retain();
            return new DescriptorLease(entry.Channel);
        }
    }

    /// <summary>Duplicates a slot, clearing OCEXEC at the destination like 9front sysdup.</summary>
    public async ValueTask<int> DuplicateAsync(int source, int destination = -1)
    {
        Entry? displaced;
        lock (gate)
        {
            Entry entry = Get(source);
            if (destination == -1) destination = FindFree();
            Grow(destination);
            entry.Channel.Retain();
            slots.Remove(destination, out displaced);
            slots[destination] = new Entry(entry.Channel, false);
        }

        if (displaced is not null) await displaced.Channel.ReleaseAsync();
        return destination;
    }

    /// <summary>Moves a descriptor to another number, closing any displaced descriptor.</summary>
    public async ValueTask RenumberAsync(int source, int destination)
    {
        Entry? displaced;
        lock (gate)
        {
            Entry entry = Get(source);
            // Grow before changing either slot so a failed allocation leaves the table intact.
            Grow(destination);
            slots.Remove(source);
            slots.Remove(destination, out displaced);
            slots[destination] = entry;
        }

        if (displaced is not null) await displaced.Channel.ReleaseAsync();
    }

    /// <summary>Detaches a slot before closing its reference; provider close errors are swallowed.</summary>
    public ValueTask CloseAsync(int fd)
    {
        Entry entry;
        lock (gate)
        {
            entry = Get(fd);
            slots.Remove(fd);
        }

        return entry.Channel.ReleaseAsync();
    }

    /// <summary>Closes marked slots at an explicit successful application exec boundary.</summary>
    public Task CloseOnExecAsync()
    {
        Entry[] detached;
        lock (gate)
        {
            EnsureOpen();
            int[] marked = slots.Where(pair => pair.Value.CloseOnExec).Select(pair => pair.Key).ToArray();
            detached = marked.Select(fd => slots[fd]).ToArray();
            foreach (int fd in marked) slots.Remove(fd);
        }

        return ReleaseEntriesAsync(detached);
    }

    internal void RetainOwner()
    {
        lock (gate)
        {
            EnsureOpen();
            owners++;
        }
    }

    internal Task ReleaseOwnerAsync()
    {
        Entry[] detached;
        lock (gate)
        {
            if (--owners != 0) return Task.CompletedTask;
            closed = true;
            detached = slots.Values.ToArray();
            slots.Clear();
        }

        return ReleaseEntriesAsync(detached);
    }

    internal DescriptorGroup Copy()
    {
        lock (gate)
        {
            EnsureOpen();
            var copy = new DescriptorGroup();
            foreach (var pair in slots)
            {
                pair.Value.Channel.Retain();
                copy.slots.Add(pair.Key, pair.Value);
            }

            // dupfgrp rounds maxfd+1 to DELTAFD; it does not preserve spare capacity.
            if (slots.Count != 0) copy.capacity = ((slots.Keys.Last() / 20) + 1) * 20;
            return copy;
        }
    }

    private static async Task ReleaseEntriesAsync(Entry[] entries)
    {
        // closefgrp and the exec loop release slots in ascending fd order.
        foreach (Entry entry in entries) await entry.Channel.ReleaseAsync();
    }

    private Entry Get(int fd)
    {
        EnsureOpen();
        return slots.TryGetValue(fd, out Entry? entry) ? entry : throw new ArgumentException("Invalid file descriptor.", nameof(fd));
    }

    private int FindFree()
    {
        int fd = 0;
        foreach (int occupied in slots.Keys)
        {
            if (occupied != fd) break;
            fd = occupied + 1;
        }
        return fd;
    }

    private void Grow(int fd)
    {
        if (fd < 0) throw new ArgumentOutOfRangeException(nameof(fd));
        if (fd < capacity) return;
        // sysfile.c:growfd checks allocated capacity, not the highest live fd.
        if (fd >= capacity + 20 || capacity >= 5000)
            throw new ArgumentOutOfRangeException(nameof(fd), "Descriptor table growth limit exceeded.");
        capacity += 20;
    }

    private void EnsureOpen()
    {
        if (closed) throw new ObjectDisposedException(nameof(DescriptorGroup));
    }

    private sealed record Entry(OpenDescriptorChannel Channel, bool CloseOnExec);
}

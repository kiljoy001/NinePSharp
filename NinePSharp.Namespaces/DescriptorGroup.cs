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

/// <summary>A local Fgrp: shared descriptor slots own references to open channels.</summary>
public sealed class DescriptorGroup
{
    private readonly object gate = new();
    private readonly SortedDictionary<int, Entry> slots = new();
    private int capacity = 20;
    private int owners;
    private bool closed;

    /// <summary>Gets the number of processes sharing this table.</summary>
    public int OwnerCount
    {
        get
        {
            lock (gate)
            {
                return owners;
            }
        }
    }

    /// <summary>Gets a value indicating whether the last process has released this table.</summary>
    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    /// <summary>Gets a stable snapshot of current slots without acquiring channel ownership.</summary>
    public IReadOnlyList<DescriptorSlot> Snapshot()
    {
        lock (gate)
        {
            return slots.Select(pair => new DescriptorSlot(pair.Key, pair.Value.Channel.Handle, pair.Value.CloseOnExec)).ToArray();
        }
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
    public int Install(
        ResourceOpenHandle handle,
        Func<ValueTask> closeAsync,
        bool closeOnExec = false,
        Func<CancellationToken, ValueTask<IReadOnlyList<ResourceStat>>>? readDirectoryAsync = null,
        string? visibleName = null,
        bool isMountPoint = false)
        => InstallWithCursor(
            handle,
            closeAsync,
            closeOnExec,
            readDirectoryAsync is null ? null : new DirectoryCursor(readDirectoryAsync),
            visibleName,
            isMountPoint);

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
            if (destination == -1)
            {
                destination = FindFree();
            }

            Grow(destination);
            entry.Channel.Retain();
            slots.Remove(destination, out displaced);
            slots[destination] = new Entry(entry.Channel, false);
        }

        if (displaced is not null)
        {
            await displaced.Channel.ReleaseAsync();
        }

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

        if (displaced is not null)
        {
            await displaced.Channel.ReleaseAsync();
        }
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
            foreach (int fd in marked)
            {
                slots.Remove(fd);
            }
        }

        return ReleaseEntriesAsync(detached);
    }

    internal int InstallWithCursor(
        ResourceOpenHandle handle,
        Func<ValueTask> closeAsync,
        bool closeOnExec,
        IDirectoryCursor? directory,
        string? visibleName = null,
        bool isMountPoint = false)
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
            if (--owners != 0)
            {
                return Task.CompletedTask;
            }

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
            if (slots.Count != 0)
            {
                copy.capacity = ((slots.Keys.Last() / 20) + 1) * 20;
            }

            return copy;
        }
    }

    private static async Task ReleaseEntriesAsync(Entry[] entries)
    {
        // closefgrp and the exec loop release slots in ascending fd order.
        foreach (Entry entry in entries)
        {
            await entry.Channel.ReleaseAsync();
        }
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
            if (occupied != fd)
            {
                break;
            }

            fd = occupied + 1;
        }

        return fd;
    }

    private void Grow(int fd)
    {
        if (fd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fd));
        }

        if (fd < capacity)
        {
            return;
        }

        // sysfile.c:growfd checks allocated capacity, not the highest live fd.
        if (fd >= capacity + 20 || capacity >= 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(fd), "Descriptor table growth limit exceeded.");
        }

        capacity += 20;
    }

    private void EnsureOpen()
    {
        if (closed)
        {
            throw new ObjectDisposedException(nameof(DescriptorGroup));
        }
    }

    private sealed record Entry(OpenDescriptorChannel Channel, bool CloseOnExec);
}

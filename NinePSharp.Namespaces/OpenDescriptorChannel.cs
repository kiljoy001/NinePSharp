namespace NinePSharp.Namespaces;

internal sealed class OpenDescriptorChannel
{
    private readonly Func<ValueTask> close;
    private readonly object positionGate = new();
    private int references = 1;
    private long offset;

    internal OpenDescriptorChannel(
        ResourceOpenHandle handle,
        Func<ValueTask> close,
        IDirectoryCursor? directory,
        string? visibleName = null,
        bool isMountPoint = false)
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
            lock (positionGate)
            {
                return offset;
            }
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
        lock (positionGate)
        {
            offset = checked(offset + actual);
        }
    }

    internal long Seek(long value, bool relative)
    {
        lock (positionGate)
        {
            long next = relative ? checked(offset + value) : value;
            if (next < 0)
            {
                throw new NamespaceFidException("negative offset");
            }

            offset = next;
            return next;
        }
    }

    // Retain is called only while an existing slot is locked and still owns a reference.
    internal void Retain() => Interlocked.Increment(ref references);

    internal async ValueTask ReleaseAsync()
    {
        if (Interlocked.Decrement(ref references) != 0)
        {
            return;
        }

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

        if (Directory is not null)
        {
            await Directory.CloseAsync();
        }
    }
}

namespace NinePSharp.Namespaces;

/// <summary>A virtual Plan 9 process group which owns a namespace mount table.</summary>
public sealed class VProcessGroup
{
    private readonly object gate = new();
    private int owners;

    public VProcessGroup(long id, MountTable? mountTable = null)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        MountTable = mountTable ?? new MountTable();
    }

    /// <summary>Gets the process-group identity.</summary>
    public long Id { get; }

    /// <summary>Gets the namespace owned by this process group.</summary>
    public MountTable MountTable { get; }

    /// <summary>Gets the number of virtual processes owning this group.</summary>
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

    internal void Retain()
    {
        lock (gate)
        {
            MountTable.EnsureOpen();
            owners++;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            if (--owners == 0)
            {
                MountTable.Close();
            }
        }
    }
}

namespace NinePSharp.Namespaces;

/// <summary>An admitted operation's reference to an open channel.</summary>
public sealed class DescriptorLease : IAsyncDisposable
{
    private readonly OpenDescriptorChannel channel;
    private int disposed;

    internal DescriptorLease(OpenDescriptorChannel channel) => this.channel = channel;

    /// <summary>Gets the provider handle retained by this operation.</summary>
    public ResourceOpenHandle Handle => channel.Handle;

    internal string? VisibleName => channel.VisibleName;

    /// <summary>Gets a value indicating whether the mount point was in place when this channel was opened.</summary>
    internal bool IsMountPoint => channel.IsMountPoint;

    /// <summary>Gets the shared implicit I/O offset for this open channel.</summary>
    internal long Offset => channel.Offset;

    internal IDirectoryCursor Directory => channel.Directory
        ?? throw new NamespaceFidException("descriptor has no directory cursor");

    /// <summary>Releases this operation's reference once, even if disposal is repeated.</summary>
    public ValueTask DisposeAsync()
        => Interlocked.Exchange(ref disposed, 1) == 0 ? channel.ReleaseAsync() : ValueTask.CompletedTask;

    internal long ReserveWrite(int length) => channel.ReserveWrite(length);

    internal void CorrectWrite(long unused) => channel.CorrectWrite(unused);

    internal void AdvanceRead(int actual) => channel.AdvanceRead(actual);

    internal long Seek(long offset, bool relative) => channel.Seek(offset, relative);
}

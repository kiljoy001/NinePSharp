using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>An object-based channel which retains visible traversal and mount crossings.</summary>
public sealed class NamespaceChannel
{
    private readonly List<ChannelFrame> frames;

    internal NamespaceChannel(IEnumerable<ChannelFrame> frames)
    {
        this.frames = frames.ToList();
        if (this.frames.Count == 0)
        {
            throw new ArgumentException("A channel must contain a root frame.", nameof(frames));
        }
    }

    /// <summary>Gets the currently selected resource.</summary>
    public ResourceHandle Current => frames[^1].Handle;

    /// <summary>Gets the current visible path.</summary>
    public IReadOnlyList<string> VisiblePath => frames.Skip(1).Select(frame => frame.Name).ToArray();

    /// <summary>Gets an immutable view of the navigation history.</summary>
    public IReadOnlyList<ChannelFrame> Frames => frames.ToArray();

    internal ChannelFrame CurrentFrame => frames[^1];

    /// <summary>Restores a channel from persisted traversal frames.</summary>
    public static NamespaceChannel Restore(IEnumerable<ChannelFrame> frames)
        => new NamespaceChannel(frames);

    /// <summary>Creates an independent channel retaining the same object references.</summary>
    public NamespaceChannel Clone() => new(frames);

    internal void Push(ChannelFrame frame) => frames.Add(frame);

    internal void ReplaceCurrent(ChannelFrame frame) => frames[^1] = frame;

    internal void UpdateCurrent(ResourceHandle handle)
    {
        ChannelFrame current = CurrentFrame;
        ReplaceCurrent(current with { Handle = handle });
    }

    internal void WalkParent()
    {
        if (frames.Count > 1)
        {
            frames.RemoveAt(frames.Count - 1);
        }
    }
}

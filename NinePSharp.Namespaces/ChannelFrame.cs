using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>One visible location retained by a channel during path traversal.</summary>
/// <param name="Name">The visible path element.</param>
/// <param name="Handle">The selected resource.</param>
/// <param name="MountedFrom">The hidden mounted-upon resource, when this frame crossed a mount.</param>
/// <param name="Union">The ordered union visible at this location.</param>
public sealed record ChannelFrame(
    string Name,
    ResourceHandle Handle,
    ResourceHandle? MountedFrom = null,
    IReadOnlyList<MountBinding>? Union = null);

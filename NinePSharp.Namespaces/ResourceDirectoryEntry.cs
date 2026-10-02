using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>A named directory entry returned by a resource provider.</summary>
/// <param name="Name">The visible entry name.</param>
/// <param name="Handle">The resource represented by the entry.</param>
public sealed record ResourceDirectoryEntry(string Name, ResourceHandle Handle);

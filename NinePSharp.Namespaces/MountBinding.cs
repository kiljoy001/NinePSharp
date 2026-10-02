namespace NinePSharp.Namespaces;

/// <summary>A member of an ordered mount list.</summary>
/// <param name="MountId">A monotonically increasing namespace-local identifier.</param>
/// <param name="Flags">The mount behavior flags.</param>
/// <param name="Target">The mounted resource.</param>
/// <param name="Spec">An optional attach specification.</param>
public sealed record MountBinding(long MountId, MountFlags Flags, ResourceHandle Target, string Spec);

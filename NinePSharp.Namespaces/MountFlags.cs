namespace NinePSharp.Namespaces;

/// <summary>
/// Controls how a resource is added to a mount point.
/// </summary>
[Flags]
public enum MountFlags
{
    /// <summary>Replace the object at the mount point.</summary>
    Replace = 0,

    /// <summary>Add the resource before the existing union members.</summary>
    Before = 1,

    /// <summary>Add the resource after the existing union members.</summary>
    After = 2,

    /// <summary>Permit creation in this union member.</summary>
    Create = 4,

    /// <summary>Permit a transport-specific data cache.</summary>
    Cache = 16,
}

internal static class MountFlagsExtensions
{
    internal const MountFlags OrderMask = MountFlags.Before | MountFlags.After;
    internal const MountFlags All = OrderMask | MountFlags.Create | MountFlags.Cache;

    internal static MountFlags Order(this MountFlags flags) => flags & OrderMask;
}

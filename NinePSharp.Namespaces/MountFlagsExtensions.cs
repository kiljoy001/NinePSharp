namespace NinePSharp.Namespaces;

internal static class MountFlagsExtensions
{
    internal const MountFlags OrderMask = MountFlags.Before | MountFlags.After;
    internal const MountFlags All = OrderMask | MountFlags.Create | MountFlags.Cache;

    internal static MountFlags Order(this MountFlags flags) => flags & OrderMask;
}

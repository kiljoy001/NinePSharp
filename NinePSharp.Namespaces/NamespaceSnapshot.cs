namespace NinePSharp.Namespaces;

/// <summary>A serializable snapshot of a namespace mount table.</summary>
/// <param name="NextMountId">The next namespace-local mount identifier.</param>
/// <param name="MountHeads">All mount points in the namespace.</param>
/// <param name="MountsDisabled">Whether service mounts are disabled for the namespace.</param>
/// <param name="BlockedMountDevices">Device names denied for service mounts.</param>
public sealed record NamespaceSnapshot(
    long NextMountId,
    IReadOnlyList<MountHead> MountHeads,
    bool MountsDisabled = false,
    IReadOnlyList<string>? BlockedMountDevices = null);

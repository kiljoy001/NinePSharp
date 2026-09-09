namespace NinePSharp.Namespaces.Orleans;

/// <summary>Provides structural equality for Orleans models which contain arrays.</summary>
public static class NamespaceModelEquality
{
    /// <summary>Compares two namespace snapshots by mount identity and ordered members.</summary>
    public static bool EquivalentTo(this NamespaceSnapshotModel left, NamespaceSnapshotModel right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.NextMountId != right.NextMountId || left.MountHeads.Length != right.MountHeads.Length)
        {
            return false;
        }

        var rightHeads = right.MountHeads.ToDictionary(head => head.From.Identity);
        return left.MountHeads.All(
            leftHead => rightHeads.TryGetValue(leftHead.From.Identity, out MountHeadModel? rightHead) &&
                        Equivalent(leftHead, rightHead));
    }

    /// <summary>Compares two channel models including their mount-crossing history.</summary>
    public static bool EquivalentTo(this NamespaceChannelModel left, NamespaceChannelModel right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Frames.Length == right.Frames.Length &&
               left.Frames.Zip(right.Frames).All(pair => Equivalent(pair.First, pair.Second));
    }

    /// <summary>Compares complete virtual process state structurally.</summary>
    public static bool EquivalentTo(this VProcessStateModel left, VProcessStateModel right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.ProcessId == right.ProcessId &&
               left.ParentId == right.ParentId &&
               StringComparer.Ordinal.Equals(left.ProcessGroupId, right.ProcessGroupId) &&
               left.Root.EquivalentTo(right.Root) &&
               left.CurrentDirectory.EquivalentTo(right.CurrentDirectory);
    }

    private static bool Equivalent(MountHeadModel left, MountHeadModel right)
        => left.From == right.From &&
           left.Mounts.Length == right.Mounts.Length &&
           left.Mounts.Zip(right.Mounts).All(pair => pair.First == pair.Second);

    private static bool Equivalent(ChannelFrameModel left, ChannelFrameModel right)
        => StringComparer.Ordinal.Equals(left.Name, right.Name) &&
           left.Handle == right.Handle &&
           left.MountedFrom == right.MountedFrom &&
           Equivalent(left.Union, right.Union);

    private static bool Equivalent(MountBindingModel[]? left, MountBindingModel[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null &&
               right is not null &&
               left.Length == right.Length &&
               left.Zip(right).All(pair => pair.First == pair.Second);
    }
}

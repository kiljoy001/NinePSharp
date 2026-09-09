using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Tests;

public sealed class MountTablePropertyTests
{
    [Property(MaxTest = 200)]
    public bool ReplacingAMountAlwaysLeavesOneVisibleTarget(NonEmptyString targetName)
    {
        string safeName = string.Concat(
            targetName.Get.Select(character => char.IsWhiteSpace(character) || character == '\0' ? '_' : character));
        var mountedOn = Directory("base", 1);
        var target = Directory(safeName, 2);
        var table = new MountTable();

        table.Mount(Directory("earlier", 3), mountedOn, MountFlags.Before);
        table.Mount(target, mountedOn, MountFlags.Replace);

        MountHead? head = table.Find(mountedOn.Identity);
        return head is not null &&
               head.Mounts.Count == 1 &&
               head.Mounts[0].Target.Identity == target.Identity;
    }

    [Property(MaxTest = 200)]
    public bool CopiedNamespacesDoNotObserveLaterReplacement(PositiveInt path)
    {
        ulong value = (ulong)path.Get;
        var mountedOn = Directory("base", value);
        var first = Directory("first", value + 1);
        var second = Directory("second", value + 2);
        var original = new MountTable();
        original.Mount(first, mountedOn);
        MountTable copy = original.Clone();

        original.Mount(second, mountedOn);

        return copy.Find(mountedOn.Identity)!.Mounts[0].Target.Identity == first.Identity;
    }

    private static ResourceHandle Directory(string device, ulong path)
        => new(new ResourceIdentity("property", device, path), QidType.QTDIR);
}

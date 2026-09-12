using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceSyscallsPropertyTests
{
    [Property(MaxTest = 200)]
    public async Task<bool> BindAlwaysTargetsTheUnderlyingFinalPath(
        NonEmptyString sourceName,
        NonEmptyString targetName)
    {
        string source = SafeName(sourceName.Get, "source") + "-source";
        string target = SafeName(targetName.Get, "target") + "-target";
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle sourceHandle = resources.AddChild(root, source, true);
        ResourceHandle targetHandle = resources.AddChild(root, target, true);
        ResourceHandle existing = resources.Directory("existing");
        var process = new VProcessTable().CreateInitial(
            new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ProcessGroup.MountTable.Mount(existing, targetHandle);
        var syscalls = new NamespaceSyscalls(resources);

        MountBinding binding = await syscalls.BindAsync(process, "/" + source, "/" + target);

        return process.ProcessGroup.MountTable.Find(targetHandle.Identity) is { Mounts.Count: 1 } head &&
               binding.Target.Identity == sourceHandle.Identity &&
               head.Mounts[0].Target.Identity == sourceHandle.Identity;
    }

    [Property(MaxTest = 200)]
    public async Task<bool> ReadOnlyMountSourcesNeverMutateTheNamespace(NonNegativeInt modeSeed)
    {
        byte mode = (byte)(modeSeed.Get & 0x03);
        if (mode == NinePConstants.ORDWR)
        {
            mode = NinePConstants.OREAD;
        }

        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle target = resources.AddChild(root, "target", true);
        ResourceHandle service = resources.Directory("service");
        var process = new VProcessTable().CreateInitial(
            new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var source = new NamespaceMountSource(
            new NamespaceNavigator(new MountTable(), resources).Attach(service),
            mode);
        var syscalls = new NamespaceSyscalls(resources);

        try
        {
            await syscalls.MountAsync(process, source, "/target");
            return false;
        }
        catch (NamespaceException exception) when (exception.Error == NamespaceError.MountSourceNotReadWrite)
        {
            return process.ProcessGroup.MountTable.Find(target.Identity) is null;
        }
    }

    private static string SafeName(string value, string fallback)
    {
        string result = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_'));
        return string.IsNullOrEmpty(result) ? fallback : result;
    }
}

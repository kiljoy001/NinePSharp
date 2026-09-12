using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceControlResourceTests
{
    [Fact]
    public async Task ProcDirectoryExposesNamespaceFilesAndStatus()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        _ = resources.AddChild(root, "source", true);
        ResourceHandle target = resources.AddChild(root, "target", true);
        var processes = new VProcessTable();
        VProcess process = processes.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var control = new NamespaceControlResource(processes, resources);

        ResourceHandle proc = await Walk(control, control.Root, "proc");
        ResourceHandle pid = await Walk(control, proc, process.Id.ToString());
        IReadOnlyList<ResourceDirectoryEntry> entries = await control.ReadDirectoryAsync(pid, CancellationToken.None);

        Assert.Equal(new[] { "ns", "status", "ctl" }, entries.Select(entry => entry.Name));
        ResourceHandle status = entries.Single(entry => entry.Name == "status").Handle;
        ResourceOpenHandle opened = await control.OpenAsync(status, NinePConstants.OREAD, Context(), CancellationToken.None);
        string text = Encoding.UTF8.GetString((await control.ReadAsync(opened, 0, 4096, CancellationToken.None)).Span);
        Assert.Contains($"pid={process.Id}", text, StringComparison.Ordinal);
        Assert.Contains($"group={process.ProcessGroup.Id}", text, StringComparison.Ordinal);
        Assert.NotNull(target);
    }

    [Fact]
    public async Task CtlBindAndRforkCommandsMutateTheSelectedProcess()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        ResourceHandle source = resources.AddChild(root, "source", true);
        ResourceHandle target = resources.AddChild(root, "target", true);
        var processes = new VProcessTable();
        VProcess process = processes.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var control = new NamespaceControlResource(processes, resources);
        ResourceHandle ctl = await Walk(control, await Walk(control, control.Root, "proc"), process.Id + "/ctl");
        ResourceOpenHandle opened = await control.OpenAsync(ctl, NinePConstants.ORDWR, Context(), CancellationToken.None);

        byte[] bind = Encoding.UTF8.GetBytes("bind /source /target replace");
        Assert.Equal((uint)bind.Length, await control.WriteAsync(opened, 0, bind, Context(), CancellationToken.None));
        Assert.Equal(source.Identity, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts[0].Target.Identity);

        byte[] rfork = Encoding.UTF8.GetBytes("rfork copy nomounts");
        await control.WriteAsync(opened, 0, rfork, Context(), CancellationToken.None);
        Assert.True(process.ProcessGroup.MountTable.MountsDisabled);
    }

    [Fact]
    public async Task CtlRejectsUnknownCommandsAndNonZeroOffsets()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        var processes = new VProcessTable();
        VProcess process = processes.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var control = new NamespaceControlResource(processes, resources);
        ResourceHandle proc = await Walk(control, control.Root, "proc");
        ResourceHandle pid = await Walk(control, proc, process.Id.ToString());
        ResourceHandle ctl = (await control.ReadDirectoryAsync(pid, CancellationToken.None)).Single(entry => entry.Name == "ctl").Handle;
        ResourceOpenHandle opened = await control.OpenAsync(ctl, NinePConstants.ORDWR, Context(), CancellationToken.None);

        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(opened, 1, "bind /a /b"u8.ToArray(), Context(), CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(opened, 0, "unknown"u8.ToArray(), Context(), CancellationToken.None).AsTask());
    }

    private static async Task<ResourceHandle> Walk(NamespaceControlResource control, ResourceHandle start, string path)
    {
        ResourceHandle current = start;
        foreach (string name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = await control.WalkAsync(current, name, CancellationToken.None)
                ?? throw new Xunit.Sdk.XunitException($"Missing control path {path}");
        }

        return current;
    }

    private static ResourceOperationContext Context()
        => new(new ResourceOperationId("control-tests", 1), 1, "tester");
}

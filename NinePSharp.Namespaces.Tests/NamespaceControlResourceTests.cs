using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceControlResourceTests
{
    [Fact]
    public async Task KillRemovesOnlyTheSelectedProcessAndRejectsArguments()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var table = new VProcessTable();
        var parent = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var child = table.Fork(parent.Id, NamespaceForkMode.Share);
        var group = parent.ProcessGroup;
        var control = new NamespaceControlResource(table, resources);
        var proc = await Walk(control, control.Root, "proc");
        var ctl = await Walk(control, proc, parent.Id + "/ctl");
        var opened = await control.OpenAsync(ctl, NinePConstants.OWRITE, Context(), default);
        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(opened, 0, Encoding.UTF8.GetBytes("kill child"), Context(), default).AsTask());
        Assert.False(parent.IsTerminated);
        Assert.Equal(4U, await control.WriteAsync(opened, 0, Encoding.UTF8.GetBytes("kill"), Context(), default));
        Assert.True(parent.IsTerminated);
        Assert.False(child.IsTerminated);
        Assert.Equal(1, group.OwnerCount);
        Assert.Null(await control.WalkAsync(proc, parent.Id.ToString(), default));
        Assert.Equal(child.Id.ToString(), Assert.Single(await control.ReadDirectoryAsync(proc, default)).Name);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => control.WriteAsync(opened, 0, Encoding.UTF8.GetBytes("kill"), Context(), default).AsTask());
        await control.ClunkAsync(opened, Context(), default);
    }

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
        ResourceStat statusStat = await control.StatAsync(status, CancellationToken.None);
        Assert.Equal("status", statusStat.Name);
        string text = Encoding.UTF8.GetString((await control.ReadAsync(opened, 0, 4096, CancellationToken.None)).Span);
        Assert.Contains($"pid={process.Id}", text, StringComparison.Ordinal);
        Assert.Contains($"group={process.ProcessGroup.Id}", text, StringComparison.Ordinal);
        ResourceHandle ns = entries.Single(entry => entry.Name == "ns").Handle;
        ResourceOpenHandle nsOpen = await control.OpenAsync(ns, NinePConstants.OREAD, Context(), CancellationToken.None);
        Assert.Contains("root=", Encoding.UTF8.GetString((await control.ReadAsync(nsOpen, 0, 4096, CancellationToken.None)).Span), StringComparison.Ordinal);
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

        byte[] unmount = Encoding.UTF8.GetBytes("unmount /target");
        await control.WriteAsync(opened, 0, unmount, Context(), CancellationToken.None);
        Assert.Null(process.ProcessGroup.MountTable.Find(target.Identity));

        byte[] rfork = Encoding.UTF8.GetBytes("rfork copy nomounts");
        await control.WriteAsync(opened, 0, rfork, Context(), CancellationToken.None);
        Assert.True(process.ProcessGroup.MountTable.MountsDisabled);

        byte[] policy = Encoding.UTF8.GetBytes("mounts-disabled off");
        await control.WriteAsync(opened, 0, policy, Context(), CancellationToken.None);
        Assert.False(process.ProcessGroup.MountTable.MountsDisabled);
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
        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(opened, 0, "mounts-disabled maybe"u8.ToArray(), Context(), CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => control.CreateAsync(ctl, "new", false, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => control.RemoveAsync(ctl, opened, Context(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task TreeMetadataAndPartialReadsDescribeTheSelectedProcess()
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var source = resources.AddChild(root, "source", true);
        var target = resources.AddChild(root, "target", true);
        var table = new VProcessTable();
        var process = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var child = table.Fork(process.Id, NamespaceForkMode.Share);
        var control = new NamespaceControlResource(table, resources);
        Assert.Throws<ArgumentNullException>(() => new NamespaceControlResource(null!, resources));
        Assert.Throws<ArgumentNullException>(() => new NamespaceControlResource(table, null!));
        Assert.Equal(new[] { "proc" }, (await control.ReadDirectoryAsync(control.Root, default)).Select(e => e.Name));
        var proc = await Walk(control, control.Root, "proc");
        Assert.Equal(new[] { process.Id.ToString(), child.Id.ToString() }, (await control.ReadDirectoryAsync(proc, default)).Select(e => e.Name));
        var pid = await Walk(control, proc, child.Id.ToString());
        foreach (var dir in new[] { control.Root, proc, pid })
        {
            Assert.True(dir.IsDirectory);
            var stat = await control.StatAsync(dir, default);
            Assert.Equal((uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755, stat.Mode);
            Assert.Equal(0UL, stat.Length);
        }
        Assert.Equal("/", (await control.StatAsync(control.Root, default)).Name);
        var status = await Walk(control, pid, "status");
        Assert.False(status.IsDirectory);
        Assert.Null(await control.WalkAsync(status, "ns", default));
        Assert.Null(await control.WalkAsync(proc, "9999", default));
        Assert.Null(await control.WalkAsync(proc, "bad", default));
        Assert.Null(await control.WalkAsync(pid, "unknown", default));
        Assert.Empty(await control.ReadDirectoryAsync(status, default));
        var opened = await control.OpenAsync(status, NinePConstants.ORDWR, Context(), default);
        Assert.Equal(NinePConstants.ORDWR, opened.Mode);
        Assert.Equal(status.Identity.Device, opened.HandleId);
        string expected = $"pid={child.Id}\nparent={process.Id}\ngroup={process.ProcessGroup.Id}\nmounts=0\nmounts-disabled=false\n";
        Assert.Equal(expected, Encoding.UTF8.GetString((await control.ReadAsync(opened, 0, uint.MaxValue, default)).Span));
        Assert.Equal(expected.Substring(1, 3), Encoding.UTF8.GetString((await control.ReadAsync(opened, 1, 3, default)).Span));
        Assert.Empty((await control.ReadAsync(opened, (ulong)expected.Length, 1, default)).ToArray());
        Assert.Empty((await control.ReadAsync(opened, ulong.MaxValue, 1, default)).ToArray());
        var metadata = await control.StatAsync(status, default);
        Assert.Equal((ulong)expected.Length, metadata.Length);
        Assert.Equal(NinePConstants.Mode0644, metadata.Mode);
        var ctl = await Walk(control, pid, "ctl");
        var writer = await control.OpenAsync(ctl, NinePConstants.OWRITE, Context(), default);
        await control.WriteAsync(writer, 0, "bind /source /target"u8.ToArray(), Context(), default);
        var binding = process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts.Single();
        var ns = await control.OpenAsync(await Walk(control, pid, "ns"), 0, Context(), default);
        string mountLine = $"mount={binding.MountId} {target.Identity.Provider}:{target.Identity.Device} -> {source.Identity.Provider}:{source.Identity.Device} flags=Replace\n";
        Assert.Equal("root=\ncwd=\n" + mountLine, Encoding.UTF8.GetString((await control.ReadAsync(ns, 0, 4096, default)).Span));
        Assert.Equal(expected[1..], Encoding.UTF8.GetString((await control.ReadAsync(opened, 1, 4096, default)).Span).Replace("mounts=1", "mounts=0", StringComparison.Ordinal));
        Assert.Empty((await control.ReadAsync(writer, 0, 1, default)).ToArray());
        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(opened, 0, "rfork empty"u8.ToArray(), Context(), default).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => control.CreateAndOpenAsync(pid, "x", 0, 0, Context(), default).AsTask());
        var missing = new ResourceHandle(new ResourceIdentity("namespace-control", "/proc/9999/status", 1), QidType.QTFILE);
        await Assert.ThrowsAsync<NamespaceException>(() => control.OpenAsync(missing, 0, Context(), default).AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("bind")]
    [InlineData("bind /source")]
    [InlineData("bind /source /target after extra")]
    [InlineData("bind /source /target invalid")]
    [InlineData("unmount")]
    [InlineData("unmount /target /source extra")]
    [InlineData("rfork")]
    [InlineData("rfork copy nomounts extra")]
    [InlineData("rfork invalid")]
    [InlineData("mounts-disabled")]
    [InlineData("mounts-disabled on extra")]
    public async Task MalformedCommandsLeaveNamespaceUnchanged(string command)
    {
        var resources = new MemoryResources();
        var table = new VProcessTable();
        var process = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(resources.Directory("root")));
        var control = new NamespaceControlResource(table, resources);
        var ctl = await control.OpenAsync(await Walk(control, control.Root, $"proc/{process.Id}/ctl"), 1, Context(), default);
        await Assert.ThrowsAsync<NamespaceException>(() => control.WriteAsync(ctl, 0, Encoding.UTF8.GetBytes(command), Context(), default).AsTask());
        Assert.Empty(process.ProcessGroup.MountTable.Snapshot().MountHeads);
        Assert.False(process.ProcessGroup.MountTable.MountsDisabled);
    }

    [Theory]
    [InlineData("share", false)]
    [InlineData("copy", false)]
    [InlineData("empty", false)]
    [InlineData("copy", true)]
    public async Task RforkPreservesOrSeparatesTheGroupAsRequested(string mode, bool noMounts)
    {
        var resources = new MemoryResources();
        var table = new VProcessTable();
        var process = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(resources.Directory("root")));
        var group = process.ProcessGroup;
        var control = new NamespaceControlResource(table, resources);
        var ctl = await control.OpenAsync(await Walk(control, control.Root, $"proc/{process.Id}/ctl"), 1, Context(), default);
        await control.WriteAsync(ctl, 0, Encoding.UTF8.GetBytes($"rfork {mode}" + (noMounts ? " nomounts" : "")), Context(), default);
        Assert.Equal(mode == "share", ReferenceEquals(group, process.ProcessGroup));
        Assert.Equal(noMounts, process.ProcessGroup.MountTable.MountsDisabled);
        foreach (var (value, expected) in new[] { ("on", true), ("off", false), ("true", true), ("false", false) })
        {
            await control.WriteAsync(ctl, 0, Encoding.UTF8.GetBytes($"  mounts-disabled  {value}\n"), Context(), default);
            Assert.Equal(expected, process.ProcessGroup.MountTable.MountsDisabled);
        }
    }

    [Fact]
    public async Task CancellationPreventsEveryControlOperation()
    {
        var resources = new MemoryResources();
        var table = new VProcessTable();
        var control = new NamespaceControlResource(table, resources);
        var opened = new ResourceOpenHandle(control.Root, "/", 0, 0);
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.WalkAsync(control.Root, "proc", token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.ReadDirectoryAsync(control.Root, token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.OpenAsync(control.Root, 0, Context(), token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.ReadAsync(opened, 0, 1, token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.WriteAsync(opened, 0, new byte[1], Context(), token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.StatAsync(control.Root, token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control.ClunkAsync(opened, Context(), token).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => control.WalkAsync(control.Root, " ", default).AsTask());
    }

    [Theory]
    [InlineData("before", MountFlags.Before)]
    [InlineData("after", MountFlags.After)]
    public async Task BindOrderAndSelectedUnmountPreserveOtherUnionMembers(string order, MountFlags expected)
    {
        var resources = new MemoryResources();
        var root = resources.Directory("root");
        var source = resources.AddChild(root, "source", true);
        var target = resources.AddChild(root, "target", true);
        var table = new VProcessTable();
        var process = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var control = new NamespaceControlResource(table, resources);
        var ctl = await control.OpenAsync(await Walk(control, control.Root, $"proc/{process.Id}/ctl"), 1, Context(), default);
        await control.WriteAsync(ctl, 0, Encoding.UTF8.GetBytes($"bind /source /target {order}"), Context(), default);
        var mounts = process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts;
        Assert.Equal(expected, mounts.Single(m => m.Target == source).Flags);
        await control.WriteAsync(ctl, 0, "unmount /target /source"u8.ToArray(), Context(), default);
        Assert.Equal(target, process.ProcessGroup.MountTable.Find(target.Identity)!.Mounts.Single().Target);
    }

    [Fact]
    public async Task ControlQidsRemainStableAcrossRecreationAndDifferBetweenPaths()
    {
        var resources = new MemoryResources();
        var table = new VProcessTable();
        var first = new NamespaceControlResource(table, resources);
        var second = new NamespaceControlResource(table, resources);
        Assert.Equal(first.Root, second.Root);
        Assert.Equal(4953208436630043972UL, first.Root.Identity.Path);
        var proc = await Walk(first, first.Root, "proc");
        Assert.NotEqual(first.Root.Identity.Path, proc.Identity.Path);
        Assert.Equal(proc, await Walk(second, second.Root, "proc"));
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

using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class FileWStatSyscallTests
{
    [Fact]
    public async Task InvalidRecordsWinOverPathAndDescriptorLookup()
    {
        await using var f = new FileStatFixture();
        byte[] malformed = new byte[48];
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WStatAsync("/absent", malformed).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.FWStatAsync(-1, malformed).AsTask());
        Assert.Empty(f.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidRecordsStillRequireAPath(string? path)
    {
        await using var f = new FileStatFixture();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Calls.WStatAsync(path!, Update(value => value)).AsTask());
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task ValidRequestsPreserveLookupAndDirectoryErrors()
    {
        await using var f = new FileStatFixture();
        byte[] update = Update(value => value);
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.WStatAsync("/absent", update).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.WStatAsync("/file/", update).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.WStatAsync("/file/.", update).AsTask());
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task CancellationBeforeAndDuringLookupPreventsMutationDispatch()
    {
        await using var f = new FileStatFixture();
        byte[] update = Update(value => value);
        using var before = new CancellationTokenSource();
        before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.WStatAsync("/file", update, before.Token).AsTask());
        int fd = await f.Calls.OpenAsync("/file", new(0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.FWStatAsync(fd, update, before.Token).AsTask());
        using var during = new CancellationTokenSource();
        f.Files.Resources.BeforeWalk = () =>
        {
            during.Cancel();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.WStatAsync("/file", update, during.Token).AsTask());
        await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.WStatAsync("/", update, before.Token).AsTask());
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task PathUpdateAppliesCurrentFinalMountAndSavedMountedUponIdentity()
    {
        await using var f = new FileStatFixture();
        f.UpdateReply = (_, bytes, _) => ValueTask.FromResult((uint)bytes.Length);
        ResourceHandle root = f.Files.Process.Root.Current;
        ResourceHandle mounted = f.Files.Resources.Directory("wstat-final-mount");
        f.Files.Process.ProcessGroup.MountTable.Mount(mounted, root);
        await f.Calls.WStatAsync("/", Update(value => value));
        Assert.Equal(mounted, f.Updates[^1].Resource);

        ResourceHandle savedMountedOn = f.Files.Resources.Directory("wstat-saved-crossing");
        f.Files.Process.ChangeDirectory(NamespaceChannel.Restore(new[]
        {
            new ChannelFrame("/", root),
            new ChannelFrame("cwd", root, savedMountedOn),
        }));
        f.Files.Process.ProcessGroup.MountTable.Mount(savedMountedOn, savedMountedOn);
        await f.Calls.WStatAsync(".", Update(value => value));
        Assert.Equal(savedMountedOn, f.Updates[^1].Resource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DescriptorModesDoNotRestrictUpdatesOrMoveOffsets(byte mode)
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(mode));
        await f.Calls.SeekAsync(fd, 71, Plan9SeekWhence.Set);
        byte[] update = Update(value => value with { ModificationTime = 44 });
        Assert.Equal((uint)update.Length, await f.Calls.FWStatAsync(fd, update));
        Assert.Equal(71, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        Assert.Equal(mode, Assert.Single(f.Updates).Open!.Mode);
    }

    [Fact]
    public async Task PathAndDescriptorUpdatesReturnProviderResultAndDispatchAllSentinels()
    {
        await using var f = new FileStatFixture();
        f.UpdateReply = (_, _, _) => ValueTask.FromResult(23U);
        byte[] update = Update(value => value);
        Assert.Equal(23U, await f.Calls.WStatAsync("/file", update));
        int fd = await f.Calls.OpenAsync("/file", new(0));
        Assert.Equal(23U, await f.Calls.FWStatAsync(fd, update));
        Assert.Equal(2, f.Updates.Count);
        Assert.All(f.Updates, request => Assert.Equal(update, request.Stat));
    }

    [Fact]
    public async Task RenameModeAndLengthAreOneAtomicProviderUpdate()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(NinePConstants.ORDWR));
        await f.Calls.PWriteAsync(fd, 0, new byte[20]);
        byte[] combined = Update(value => value with { Name = "renamed", Mode = NinePConstants.Mode0600, Length = 3 });
        Assert.Equal((uint)combined.Length, await f.Calls.FWStatAsync(fd, combined));
        ResourceStat changed = await f.Files.Resources.StatAsync(
            (await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "renamed", default))!, default);
        Assert.Null(await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "file", default));
        Assert.Equal(NinePConstants.Mode0600, changed.Mode);
        Assert.Equal(3UL, changed.Length);
        Assert.Equal("file", FileStatSyscallTests.Name(await f.Calls.FStatAsync(fd, 4096)));
        Assert.Equal("renamed", FileStatSyscallTests.Name(await f.Calls.StatAsync("/renamed", 4096)));
    }

    [Fact]
    public async Task ForbiddenCombinedChangeIsRejectedWithoutPartialModeMutation()
    {
        await using var f = new FileStatFixture();
        ResourceHandle file = (await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "file", default))!;
        byte[] combined = Update(value => value with { Name = "other", Mode = NinePConstants.Mode0600 });
        await Assert.ThrowsAsync<ResourceWStatRejectedException>(() => f.Calls.WStatAsync("/file", combined).AsTask());
        ResourceStat unchanged = await f.Files.Resources.StatAsync(file, default);
        Assert.Equal(NinePConstants.Mode0644, unchanged.Mode);
        Assert.Equal(file, await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "file", default));
    }

    [Fact]
    public async Task MountPointMarkerSurvivesUnmountAndLaterMountDoesNotMarkOldDescriptor()
    {
        await using var f = new FileStatFixture();
        ResourceHandle root = f.Files.Process.Root.Current;
        ResourceHandle file = await f.Files.Resources.CreateAsync(root, "dir", true, default);
        ResourceHandle other = f.Files.Resources.Directory("mounted-wstat");
        MountTable mounts = f.Files.Process.ProcessGroup.MountTable;
        mounts.Mount(other, file);
        int mountedFd = await f.Calls.OpenAsync("/dir", new(0));
        mounts.Unmount(file);
        byte[] rename = Update(value => value with { Name = "new-name" });
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.FWStatAsync(mountedFd, rename).AsTask());
        Assert.Equal(49U, await f.Calls.FWStatAsync(mountedFd, Update(value => value)));

        int ordinaryFd = await f.Calls.OpenAsync("/dir", new(0));
        mounts.Mount(other, file);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WStatAsync("/dir", rename).AsTask());
        Assert.Equal((uint)rename.Length, await f.Calls.FWStatAsync(ordinaryFd, rename));
        Assert.Equal(file, await f.Files.Resources.WalkAsync(root, "new-name", default));
    }

    [Fact]
    public async Task FwstatRejectsOnlyMountTransportHandles()
    {
        await using var f = new FileStatFixture();
        ResourceHandle resource = f.Files.Process.Root.Current;
        var transport = new ResourceOpenHandle(resource, "transport", 0, 0, isMountTransport: true);
        int fd = f.Files.Process.Descriptors.Install(transport, () => ValueTask.CompletedTask);
        byte[] update = Update(value => value);
        Assert.True((await f.Calls.FStatAsync(fd, 4096)).Length >= 49);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.FWStatAsync(fd, update).AsTask());
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task ValidLongNameReachesProviderWhileShortSlashIsRejectedBeforeDispatch()
    {
        await using var f = new FileStatFixture();
        f.UpdateReply = (_, bytes, _) => ValueTask.FromResult((uint)bytes.Length);
        byte[] longName = Update(value => value with { Name = new string('x', 62) + "/y" });
        Assert.Equal((uint)longName.Length, await f.Calls.WStatAsync("/file", longName));
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.WStatAsync(
            "/file", Update(value => value with { Name = "x/y" })).AsTask());
        Assert.Single(f.Updates);
    }

    [Fact]
    public async Task CallerMutationCannotChangeAdmittedUpdate()
    {
        await using var files = new FileSyscallFixture();
        var provider = new BlockingUpdates();
        var calls = new Plan9FileSyscalls(files.Process, files.Plane, files.Context, fileStats: provider);
        byte[] update = Update(value => value with { Mode = NinePConstants.Mode0600 });
        byte[] expected = update.ToArray();
        Task<uint> pending = calls.WStatAsync("/file", update).AsTask();
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Array.Fill(update, (byte)0);
        provider.Resume.SetResult();
        Assert.Equal((uint)expected.Length, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(expected, provider.Observed);
    }

    [Fact]
    public async Task PendingDescriptorUpdateRetainsOriginalHandleAcrossCloseReuseAndExit()
    {
        await using var f = new FileStatFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.UpdateReply = async (_, bytes, _) =>
        {
            entered.SetResult();
            await resume.Task;
            return (uint)bytes.Length;
        };
        int fd = await f.Calls.OpenAsync("/file", new(0));
        ResourceHandle original = f.Files.Process.Descriptors.Snapshot().Single().Handle.Resource;
        Task<uint> pending = f.Calls.FWStatAsync(fd, Update(value => value)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await f.Files.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(0, f.Files.Resources.ClunkCount);
        Assert.Equal(fd, await f.Calls.OpenAsync("/other", new(0)));
        await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        Assert.Equal(1, f.Files.Resources.ClunkCount);
        resume.SetResult();
        Assert.Equal(49U, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, f.Files.Resources.ClunkCount);
        Assert.Equal(original, Assert.Single(f.Updates).Open!.Resource);
    }

    private static byte[] Update(Func<ResourceWStat, ResourceWStat> change)
        => FileStatOperations.EncodeUpdate(change(ResourceWStat.Unchanged()));

    private sealed class BlockingUpdates : IFileStatOperations
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal byte[] Observed { get; private set; } = Array.Empty<byte>();

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async ValueTask<uint> WStatAsync(
            ResourceHandle resource,
            ReadOnlyMemory<byte> stat,
            ResourceOperationContext context,
            CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Resume.Task;
            Observed = stat.ToArray();
            return (uint)stat.Length;
        }

        public ValueTask<uint> WStatAsync(
            ResourceOpenHandle handle,
            ReadOnlyMemory<byte> stat,
            ResourceOperationContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

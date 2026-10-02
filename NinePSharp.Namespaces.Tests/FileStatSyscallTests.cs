using System.Buffers.Binary;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class FileStatSyscallTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task OpenModesDoNotRestrictMetadataAndOffsetsAreUnchanged(byte mode)
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(mode));
        await f.Calls.SeekAsync(fd, 73, Plan9SeekWhence.Set);
        int dup = await f.Files.Process.Descriptors.DuplicateAsync(fd);
        var child = f.Files.Table.Fork(f.Files.Process.Id, NamespaceForkMode.Copy, descriptorMode: DescriptorForkMode.Copy);
        foreach (var target in new[] { (f.Calls, fd), (f.Calls, dup), (f.ForProcess(child), fd) })
        {
            Assert.Equal("file", Name(await target.Item1.FStatAsync(target.Item2, 4096)));
            Assert.Equal(73, await target.Item1.SeekAsync(target.Item2, 0, Plan9SeekWhence.Current));
        }

        Assert.All(f.Requests, request => Assert.Equal(mode, request.Open!.Mode));
        Assert.Equal(0, f.Files.Resources.ClunkCount);
    }

    [Fact]
    public async Task FreshMetadataReflectsWritesWithoutMovingSharedPosition()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(2));
        Assert.Equal(0UL, Length(await f.Calls.FStatAsync(fd, 4096)));
        await f.Calls.PWriteAsync(fd, 5, new byte[3]);
        Assert.Equal(8UL, Length(await f.Calls.FStatAsync(fd, 4096)));
        Assert.Equal(8UL, Length(await f.Calls.StatAsync("/file", 4096)));
        Assert.Equal(0, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathStatResolvesFinalMountEvenWithoutWalkElements(bool relative)
    {
        await using var f = new FileStatFixture();
        ResourceHandle mounted = f.Files.Resources.Directory("mounted", "entry");
        if (relative)
        {
            f.Files.Process.ChangeDirectory(NamespaceChannel.Restore(new[]
            {
                new ChannelFrame("/", f.Files.Process.Root.Current), new ChannelFrame("here", mounted),
            }));
        }

        ResourceHandle target = relative ? mounted : f.Files.Process.Root.Current;
        ResourceHandle replacement = relative ? f.Files.Process.Root.Current : mounted;
        f.Files.Process.ProcessGroup.MountTable.Mount(replacement, target);
        Assert.Equal(relative ? "here" : string.Empty, Name(await f.Calls.StatAsync(relative ? "." : "/", 4096)));
        Assert.Equal(replacement, Assert.Single(f.Requests).Resource);
        Assert.Empty(f.Files.Process.Descriptors.Snapshot());
    }

    [Fact]
    public async Task UnionStatSelectsFirstResourceWithoutListingOrFallback()
    {
        await using var f = new FileStatFixture();
        var root = f.Files.Process.Root.Current;
        var mounted = f.Files.Resources.Directory("mounted");
        var mounts = f.Files.Process.ProcessGroup.MountTable;
        mounts.Mount(mounted, root);
        mounts.Mount(root, root, MountFlags.After);
        f.Reply = (_, _) => throw new IOException("stat rejected");
        await Assert.ThrowsAsync<IOException>(() => f.Calls.StatAsync("/", 4096).AsTask());
        Assert.Equal(mounted, Assert.Single(f.Requests).Resource);
        Assert.Empty(f.Files.Process.Descriptors.Snapshot());
    }

    [Fact]
    public async Task RetainedHandleAndAliasSurviveUnmountAndReplacement()
    {
        await using var f = new FileStatFixture();
        var root = f.Files.Process.Root.Current;
        var file = (await f.Files.Resources.WalkAsync(root, "file", default))!;
        var other = (await f.Files.Resources.WalkAsync(root, "other", default))!;
        var mounts = f.Files.Process.ProcessGroup.MountTable;
        mounts.Mount(NamespaceChannel.Restore(new[] { new ChannelFrame("/", other) }), file);
        int fd = await f.Calls.OpenAsync("/file", new(0));
        mounts.Unmount(file);
        Assert.Equal("file", Name(await f.Calls.FStatAsync(fd, 4096)));
        Assert.Equal(other, f.Requests[^1].Resource);
        Assert.Equal("file", Name(await f.Calls.StatAsync("/file", 4096)));
        Assert.Equal(file, f.Requests[^1].Resource);
        Assert.NotNull(f.Requests[0].Open);
        Assert.Null(f.Requests[1].Open);
    }

    [Fact]
    public async Task ManualDescriptorsWithoutSavedNamesPreserveProviderName()
    {
        await using var f = new FileStatFixture();
        var handle = new ResourceOpenHandle(f.Files.Process.Root.Current, "manual", 0, 0);
        int fd = f.Files.Process.Descriptors.Install(handle, () => ValueTask.CompletedTask);
        Assert.Equal("/", Name(await f.Calls.FStatAsync(fd, 4096)));
        int named = f.Files.Process.Descriptors.Install(handle, () => ValueTask.CompletedTask, visibleName: "alias");
        Assert.Equal("alias", Name(await f.Calls.FStatAsync(named, 4096)));
    }

    [Fact]
    public async Task CreateAndRootOpenSaveTheirVisibleNames()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.CreateAsync("/made", new(0x180, 2));
        Assert.Equal("made", Name(await f.Calls.FStatAsync(fd, 4096)));
        int root = await f.Calls.OpenAsync("/", new(0));
        Assert.Equal(string.Empty, Name(await f.Calls.FStatAsync(root, 4096)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ShortBuffersFailBeforeProviderDispatch(uint count)
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(0));
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.StatAsync("/file", count).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.FStatAsync(fd, count).AsTask());
        Assert.Empty(f.Requests);
    }

    [Theory]
    [InlineData("/absent")]
    [InlineData("/file/absent")]
    [InlineData("/file/")]
    [InlineData("/file/.")]
    public async Task InvalidPathsFailWithoutProviderMetadata(string path)
    {
        await using var f = new FileStatFixture();
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.StatAsync(path, 4096).AsTask());
        Assert.Empty(f.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidPathArgumentsAreRejected(string? path)
    {
        await using var f = new FileStatFixture();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Calls.StatAsync(path!, 4096).AsTask());
        Assert.Empty(f.Requests);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task InvalidDescriptorsFailBeforeProviderDispatch(int fd)
    {
        await using var f = new FileStatFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Calls.FStatAsync(fd, 4096).AsTask());
        Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task StatRequiresExplicitProviderConfigurationAndDoesNotLeakLease()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Calls.StatAsync("/file", 4096).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Calls.FStatAsync(fd, 4096).AsTask());
        await f.Process.Descriptors.CloseAsync(fd);
        Assert.Equal(1, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task CancelledAdmissionDoesNotDispatchAndExitRejectsNewWork()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/file", new(0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.StatAsync("/file", 4096, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.FStatAsync(fd, 4096, cancellation.Token).AsTask());
        await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.StatAsync("/", 4096, cancellation.Token).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.StatAsync("/file", 4096).AsTask());
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.FStatAsync(fd, 4096).AsTask());
        Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task AbsoluteAndRelativePathsCaptureDistinctDirectoriesAndReapplySavedMountCrossings()
    {
        await using var f = new FileStatFixture();
        ResourceHandle root = f.Files.Process.Root.Current;
        ResourceHandle current = f.Files.Resources.Directory("mounted", "file");
        f.Files.Process.ChangeDirectory(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root), new ChannelFrame("cwd", current) }));
        await f.Calls.StatAsync("/", 4096);
        Assert.Equal(root, f.Requests[^1].Resource);
        await f.Calls.StatAsync(".", 4096);
        Assert.Equal(current, f.Requests[^1].Resource);
        var rootFile = (await f.Files.Resources.WalkAsync(root, "file", default))!;
        var cwdFile = (await f.Files.Resources.WalkAsync(current, "file", default))!;
        await f.Calls.StatAsync("/file", 4096);
        Assert.Equal(rootFile, f.Requests[^1].Resource);
        await f.Calls.StatAsync("file", 4096);
        Assert.Equal(cwdFile, f.Requests[^1].Resource);

        // A retained traversal frame remembers the mounted-upon identity. A new
        // final crossing must consult that identity rather than the old target.
        f.Files.Process.ProcessGroup.MountTable.Mount(root, current);
        f.Files.Process.ChangeDirectory(NamespaceChannel.Restore(new[]
        {
            new ChannelFrame("/", root), new ChannelFrame("cwd", root, current),
        }));
        f.Files.Process.ProcessGroup.MountTable.Mount(current, current);
        Assert.Equal("cwd", Name(await f.Calls.StatAsync(".", 4096)));
        Assert.Equal(current, f.Requests[^1].Resource);
    }

    [Fact]
    public async Task CancellationDuringLookupPreventsStatDispatch()
    {
        await using var f = new FileStatFixture();
        using var cancellation = new CancellationTokenSource();
        f.Files.Resources.BeforeWalk = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Calls.StatAsync("/file", 4096, cancellation.Token).AsTask());
        Assert.Empty(f.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingFstatRetainsHandleAcrossCloseReuseExitAndCallerCancellation(bool reject)
    {
        await using var f = new FileStatFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reply = async (resource, _) =>
        {
            entered.SetResult();
            await resume.Task;
            if (reject)
            {
                throw new IOException("rejected");
            }

            return f.Record(resource, "provider");
        };
        int fd = await f.Calls.OpenAsync("/file", new(0));
        using var cancellation = new CancellationTokenSource();
        Task<ReadOnlyMemory<byte>> pending = f.Calls.FStatAsync(fd, 4096, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            cancellation.Cancel();
            await f.Files.Process.Descriptors.CloseAsync(fd);
            Assert.Equal(0, f.Files.Resources.ClunkCount);
            Assert.Equal(fd, await f.Calls.OpenAsync("/other", new(0)));
            await f.Files.Table.TerminateAsync(f.Files.Process.Id);
            Assert.Equal(1, f.Files.Resources.ClunkCount);
        }
        finally
        {
            resume.TrySetResult();
        }

        if (reject)
        {
            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        else
        {
            Assert.Equal("file", Name(await pending.WaitAsync(TimeSpan.FromSeconds(2))));
        }

        Assert.Equal(2, f.Files.Resources.ClunkCount);
        Assert.Equal(Assert.Single(f.Requests).Resource, f.Requests[0].Open!.Resource);
    }

    [Fact]
    public async Task PendingPathStatDoesNotResolveAgainAfterMountReplacementAndExit()
    {
        await using var f = new FileStatFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reply = async (resource, _) =>
        {
            entered.SetResult();
            await resume.Task;
            return f.Record(resource, "provider");
        };
        var root = f.Files.Process.Root.Current;
        Task<ReadOnlyMemory<byte>> pending = f.Calls.StatAsync("/", 4096).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            f.Files.Process.ProcessGroup.MountTable.Mount(f.Files.Resources.Directory("mounted"), root);
            await f.Files.Table.TerminateAsync(f.Files.Process.Id);
        }
        finally
        {
            resume.TrySetResult();
        }

        Assert.Equal(string.Empty, Name(await pending.WaitAsync(TimeSpan.FromSeconds(2))));
        Assert.Equal(root, Assert.Single(f.Requests).Resource);
    }

    [Fact]
    public async Task DirectoryStatDoesNotConsumeOrRewindItsCursor()
    {
        await using var f = new FileStatFixture();
        int fd = await f.Calls.OpenAsync("/", new(0));
        byte[] first = (await f.Calls.ReadAsync(fd, 4096)).ToArray();
        Assert.NotEmpty(first);
        Assert.Equal(string.Empty, Name(await f.Calls.FStatAsync(fd, 4096)));
        Assert.Empty((await f.Calls.ReadAsync(fd, 4096)).ToArray());
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        Assert.Equal(first, (await f.Calls.ReadAsync(fd, 4096)).ToArray());
    }

    [Property(MaxTest = 100)]
    public bool BoundedRepliesPreserveSizeNamesAndUntouchedMetadata(NonNegativeInt value)
    {
        using var f = new SyncFixture();
        string sourceName = new('x', value.Get % 40);
        string visible = string.Concat(Enumerable.Repeat("é", value.Get % 23));
        byte[] original = f.Value.Record(f.Value.Files.Process.Root.Current, sourceName, 100);
        uint count = (uint)(2 + (value.Get % 130));
        ReadOnlyMemory<byte> bounded = original.Length > count ? original.AsMemory(0, 2) : original;
        var actual = FileStatRecords.Rewrite(bounded, count, visible);
        int expectedSize = 100 - sourceName.Length + System.Text.Encoding.UTF8.GetByteCount(visible);
        Assert.Equal(original.Length > count ? 98 : expectedSize - 2, BinaryPrimitives.ReadUInt16LittleEndian(actual.Span));
        Assert.Equal(original.Length > count || expectedSize > count ? 2 : expectedSize, actual.Length);
        if (actual.Length > 2)
        {
            Assert.Equal(visible, Name(actual));
            Assert.Equal(original.AsSpan(2, 39).ToArray(), actual.Span.Slice(2, 39).ToArray());
            Assert.Equal(37UL, Length(actual));
        }

        return true;
    }

    internal static string Name(ReadOnlyMemory<byte> record)
        => System.Text.Encoding.UTF8.GetString(record.Span.Slice(43, BinaryPrimitives.ReadUInt16LittleEndian(record.Span[41..])));

    private static ulong Length(ReadOnlyMemory<byte> record) => BinaryPrimitives.ReadUInt64LittleEndian(record.Span[33..]);

    private sealed class SyncFixture : IDisposable
    {
        internal FileStatFixture Value { get; } = new();

        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

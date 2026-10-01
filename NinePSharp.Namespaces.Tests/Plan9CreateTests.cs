using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class Plan9CreateTests
{
    private static Plan9CreateRequest Request(int flags = 0) => new(NinePConstants.Mode0600, NinePConstants.ORDWR | flags);

    [Fact]
    public async Task CreateValidatesPathAndStartsAbsolutePathsAtRoot()
    {
        await using var f = new FileSyscallFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Calls.CreateAsync(" ", Request()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Calls.CreateAsync(null!, Request()).AsTask());
        var elsewhere = f.Resources.Directory("elsewhere");
        f.Process.ChangeDirectory(await f.Plane.AttachAsync(elsewhere, default));
        int absolute = await f.Calls.CreateAsync("/absolute", Request());
        int relative = await f.Calls.CreateAsync("relative", Request());
        Assert.Equal("root", f.Process.Descriptors.Snapshot()[absolute].Handle.Resource.Identity.Device);
        Assert.Equal("elsewhere", f.Process.Descriptors.Snapshot()[relative].Handle.Resource.Identity.Device);
        Assert.False(f.Process.Descriptors.Snapshot()[absolute].CloseOnExec);
    }

    [Fact]
    public async Task CreateAfterCancelledProviderWaitDoesNotTryToTruncate()
    {
        await using var f = new FileSyscallFixture();
        f.Plane.BeforeCreate = () => throw new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Calls.CreateAsync("new", Request()).AsTask());
        Assert.Empty(f.Process.Descriptors.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnionWithoutCreatePermissionOnlyRetriesVisibleNameForOrdinaryCreate(bool exclusive)
    {
        await using var f = new FileSyscallFixture();
        var upper = f.Resources.Directory("upper");
        f.Process.ProcessGroup.MountTable.Mount(upper, f.Process.Root.Current);
        f.Plane.BeforeCreate = async () => await f.Resources.CreateAsync(upper, "race", false, default);
        if (exclusive)
        {
            var error = await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync("race", Request(NinePConstants.OEXCL)).AsTask());
            Assert.Equal(NamespaceError.CreateNotPermitted, error.Error);
            Assert.Empty(f.Process.Descriptors.Snapshot());
        }
        else
        {
            int fd = await f.Calls.CreateAsync("race", Request());
            Assert.Equal("upper", f.Process.Descriptors.Snapshot()[fd].Handle.Resource.Identity.Device);
        }
    }

    [Fact]
    public async Task NewCreatePreservesFlagsPermissionsAndUsesLowestFreeDescriptor()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.CreateAsync("./new", Request(NinePConstants.OCEXEC | NinePConstants.ORCLOSE | NinePConstants.OEXCL));
        Assert.Equal(0, fd);
        var slot = Assert.Single(f.Process.Descriptors.Snapshot());
        Assert.True(slot.CloseOnExec);
        Assert.Equal(NinePConstants.ORDWR | NinePConstants.ORCLOSE, slot.Handle.Mode);
        ResourceStat stat = await f.Local.StatAsync(slot.Handle, default);
        Assert.Equal(NinePConstants.Mode0600, stat.Mode);
        Assert.Equal("scott", stat.User);
        Assert.Equal("new", stat.Name);
        await f.Calls.WriteAsync(fd, "new data"u8.ToArray());
        Assert.Equal(8, await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Current));
        await f.Process.Descriptors.CloseOnExecAsync();
        Assert.Equal(1, f.Resources.ClunkCount);
    }

    [Fact]
    public async Task ExistingCreateTruncatesSameResourceWithoutChangingMetadataOrExistingPosition()
    {
        await using var f = new FileSyscallFixture();
        int original = await f.OpenAsync();
        await f.Calls.WriteAsync(original, "original data"u8.ToArray());
        var beforeHandle = Assert.Single(f.Process.Descriptors.Snapshot()).Handle;
        ResourceStat before = await f.Local.StatAsync(beforeHandle, default);
        int created = await f.Calls.CreateAsync("/file", new(0, NinePConstants.OREAD | NinePConstants.OCEXEC));
        var afterHandle = f.Process.Descriptors.Snapshot()[created].Handle;
        Assert.Equal(beforeHandle.Resource.Identity, afterHandle.Resource.Identity);
        Assert.NotEqual(beforeHandle.HandleId, afterHandle.HandleId);
        Assert.Equal(NinePConstants.OTRUNC, afterHandle.Mode);
        Assert.Equal(before with { Length = 0 }, await f.Local.StatAsync(afterHandle, default));
        Assert.Equal(0, await f.Calls.SeekAsync(created, 0, Plan9SeekWhence.Current));
        Assert.Equal(13, await f.Calls.SeekAsync(original, 0, Plan9SeekWhence.Current));
        Assert.True(f.Process.Descriptors.Snapshot()[created].CloseOnExec);
    }

    [Fact]
    public async Task ExclusiveCollisionNeverOpensOrTruncates()
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.OpenAsync();
        await f.Calls.WriteAsync(fd, "keep"u8.ToArray());
        f.Plane.AfterOpen = _ => throw new IOException("must not open");
        NamespaceException error = await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync("/file", Request(NinePConstants.OEXCL)).AsTask());
        Assert.Equal(NamespaceError.ResourceAlreadyExists, error.Error);
        Assert.Equal("keep"u8.ToArray(), (await f.Calls.PReadAsync(fd, 0, 20)).ToArray());
        Assert.Single(f.Process.Descriptors.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentCreateUsesAtomicProviderCollisionWithNativeFallback(bool exclusive)
    {
        await using var f = new FileSyscallFixture();
        var child = f.Table.Fork(f.Process.Id, NamespaceForkMode.Share, descriptorMode: DescriptorForkMode.Empty);
        var secondCalls = f.ForProcess(child);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        f.Plane.BeforeCreate = () => { attempts++; return resume.Task; };
        Plan9CreateRequest request = Request(exclusive ? NinePConstants.OEXCL : 0);
        Task<int> first = f.Calls.CreateAsync("/race", request).AsTask();
        Task<int> second = secondCalls.CreateAsync("/race", request).AsTask();
        Assert.Equal(2, attempts);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        resume.SetResult();
        Task<int>[] tasks = { first, second };
        if (exclusive)
        {
            await Assert.ThrowsAsync<ResourceCreateRejectedException>(() => Task.WhenAll(tasks));
            _ = Assert.Single(tasks.Where(x => x.IsCompletedSuccessfully));
            _ = Assert.Single(tasks.Where(x => x.IsFaulted));
        }
        else
        {
            await Task.WhenAll(tasks);
            var firstHandle = Assert.Single(f.Process.Descriptors.Snapshot()).Handle;
            var secondHandle = Assert.Single(child.Descriptors.Snapshot()).Handle;
            Assert.Equal(firstHandle.Resource.Identity, secondHandle.Resource.Identity);
            Assert.NotEqual(firstHandle.HandleId, secondHandle.HandleId);
            Assert.Equal(new byte[] { 2, 18 }, new[] { firstHandle.Mode, secondHandle.Mode }.Order().ToArray());
        }
        Assert.NotNull(await f.Resources.WalkAsync(f.Process.Root.Current, "race", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedCreateRetriesOnlyForAcknowledgedRejection(bool definite)
    {
        await using var f = new FileSyscallFixture();
        ResourceHandle? raced = null;
        f.Plane.BeforeCreate = async () =>
        {
            var handle = await f.Resources.CreateAndOpenAsync(f.Process.Root.Current, "race", 0x180, 2, f.Context(), default);
            raced = handle.Resource;
            await f.Resources.WriteAsync(handle, 0, "winner"u8.ToArray(), f.Context(), default);
            if (definite) throw new ResourceCreateRejectedException("collision");
            throw new IOException("lost reply");
        };
        if (definite)
        {
            int fd = await f.Calls.CreateAsync("race", Request());
            Assert.Empty((await f.Calls.ReadAsync(fd, 6)).ToArray());
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => f.Calls.CreateAsync("race", Request()).AsTask());
            Assert.Empty(f.Process.Descriptors.Snapshot());
            Assert.Equal(6UL, (await f.Resources.StatAsync(raced!, default)).Length);
        }
    }

    [Fact]
    public async Task RejectedCreateAndMissingFallbackPreservesOriginalError()
    {
        await using var f = new FileSyscallFixture();
        var error = new ResourceCreateRejectedException("permission denied");
        f.Plane.BeforeCreate = () => throw error;
        Assert.Same(error, await Assert.ThrowsAsync<ResourceCreateRejectedException>(() => f.Calls.CreateAsync("new", Request()).AsTask()));
        Assert.Empty(f.Process.Descriptors.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailureClosesCreatedHandleWithoutRollingBackFile(bool exit)
    {
        await using var f = new FileSyscallFixture();
        VProcess? child = null;
        var root = f.Process.Root.Current;
        if (exit)
        {
            child = f.Table.Fork(f.Process.Id, NamespaceForkMode.Share);
            f.Plane.AfterCreate = async _ => await f.Table.TerminateAsync(f.Process.Id);
            await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync("new", Request()).AsTask());
            Assert.Empty(child.Descriptors.Snapshot());
        }
        else
        {
            for (int i = 0; i < 5000; i++) f.Process.Descriptors.Install(DescriptorGroupTests.Handle(i.ToString()), () => ValueTask.CompletedTask);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Calls.CreateAsync("new", Request()).AsTask());
        }
        Assert.Equal(1, f.Resources.ClunkCount);
        Assert.NotNull(await f.Resources.WalkAsync(root, "new", default));
    }

    [Fact]
    public async Task LastOwnerExitDuringProviderCreateStillReleasesReturnedHandle()
    {
        await using var f = new FileSyscallFixture();
        var root = f.Process.Root.Current;
        f.Resources.AfterCreate = async () => await f.Table.TerminateAsync(f.Process.Id);
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync("new", Request()).AsTask());
        Assert.Equal(1, f.Resources.ClunkCount);
        Assert.NotNull(await f.Resources.WalkAsync(root, "new", default));
    }

    [Theory]
    [InlineData("/new/", 0, 2)]
    [InlineData("/new/.", 0, 2)]
    [InlineData("/new", 0x800001ed, 2)]
    [InlineData("/new", 0, 0x80)]
    [InlineData("/new", 0, 0x2000)]
    [InlineData("/new", 0, -1)]
    public async Task InvalidCreateArgumentsDoNotReachProvider(string path, uint permissions, int mode)
    {
        await using var f = new FileSyscallFixture();
        f.Plane.BeforeCreate = () => throw new IOException("must not create");
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.CreateAsync(path, new(permissions, mode)).AsTask());
        Assert.Empty(f.Process.Descriptors.Snapshot());
    }

    [Theory]
    [InlineData("/", NamespaceError.ResourceAlreadyExists)]
    [InlineData(".", NamespaceError.ResourceAlreadyExists)]
    [InlineData("/missing/new", NamespaceError.ResourceNotFound)]
    [InlineData("/file/new", NamespaceError.ResourceNotDirectory)]
    public async Task InvalidParentOrRootReturnsNamespaceError(string path, NamespaceError expected)
    {
        await using var f = new FileSyscallFixture();
        var error = await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync(path, new(0x800001ed, 0)).AsTask());
        Assert.Equal(expected, error.Error);
    }

    [Theory]
    [InlineData("/dir/")]
    [InlineData("/dir/.")]
    public async Task DirectoryCreateAcceptsDirectoryFlagAndExecClose(string path)
    {
        await using var f = new FileSyscallFixture();
        await f.Calls.CreateAsync(path, new(0x800001ed, NinePConstants.OCEXEC));
        var slot = Assert.Single(f.Process.Descriptors.Snapshot());
        Assert.True(slot.Handle.Resource.IsDirectory);
        Assert.True(slot.CloseOnExec);
        Assert.Equal(0, slot.Handle.Mode);
        await Assert.ThrowsAsync<NamespaceFidException>(() => f.Calls.CreateAsync("/dir", new(0x800001ed, 0)).AsTask());
    }

    [Fact]
    public async Task CreateTruncatesVisibleUnionFileAndCreatesNewNamesOnlyInCreateMember()
    {
        await using var f = new FileSyscallFixture();
        var upper = f.Resources.Directory("upper", "existing");
        var lower = f.Resources.Directory("lower");
        var mounts = f.Process.ProcessGroup.MountTable;
        mounts.Mount(upper, f.Process.Root.Current);
        mounts.Mount(lower, f.Process.Root.Current, MountFlags.After | MountFlags.Create);
        int existing = await f.Calls.CreateAsync("/existing", Request());
        int added = await f.Calls.CreateAsync("/new", Request());
        Assert.Equal("upper", f.Process.Descriptors.Snapshot()[existing].Handle.Resource.Identity.Device);
        Assert.Equal("lower", f.Process.Descriptors.Snapshot()[added].Handle.Resource.Identity.Device);
    }

    [Property(MaxTest = 100)]
    public async Task ExclusiveCollisionPreservesGeneratedPayload(NonEmptyArray<byte> payload)
    {
        await using var f = new FileSyscallFixture();
        int fd = await f.Calls.CreateAsync("/new", Request(NinePConstants.OEXCL));
        await f.Calls.WriteAsync(fd, payload.Get);
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.CreateAsync("/new", Request(NinePConstants.OEXCL)).AsTask());
        Assert.Equal(payload.Get, (await f.Calls.PReadAsync(fd, 0, (uint)payload.Get.Length)).ToArray());
    }
}

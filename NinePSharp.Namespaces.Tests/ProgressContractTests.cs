using System.Reflection;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCollectionOrderer("NinePSharp.Namespaces.Tests.ProgressFirstOrderer", "NinePSharp.Namespaces.Tests")]

namespace NinePSharp.Namespaces.Tests;

[Collection("Namespace progress contracts")]
public sealed class ProgressContractTests
{
    [Fact]
    public async Task CompletionAndGateReleaseAreObservableBeforeAnyFurtherOperationWaits()
    {
        // Empty-process termination is synchronous: no provider callback can delay it.
        var table = new VProcessTable();
        var resource = new ResourceHandle(new("progress", "root", 1), NinePSharp.Constants.QidType.QTDIR);
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", resource) }));
        table.Terminate(process.Id);
        Assert.True(process.TerminationCompletion.IsCompletedSuccessfully, "Termination must publish its completion.");
        Assert.Throws<NamespaceException>(() => process.Root);

        var metadata = new DirectoryCursor(_ => ValueTask.FromResult<IReadOnlyList<ResourceStat>>(Array.Empty<ResourceStat>()));
        var mounts = new MountTable();
        await metadata.ReadAsync(0, 100, default, mounts);
        AssertAvailable(CursorGate(metadata));
        await metadata.RewindAsync(default);
        AssertAvailable(CursorGate(metadata));

        // A supplied directory loader must never give a regular-file descriptor
        // directory state or make its close wait for an unrelated directory cursor.
        var descriptors = new DescriptorGroup();
        var regular = new ResourceHandle(new("progress", "root", 2), 0);
        int regularFd = descriptors.Install(
            new ResourceOpenHandle(regular, "regular", 0, 0),
            () => ValueTask.CompletedTask,
            readDirectoryAsync:
            _ => ValueTask.FromResult<IReadOnlyList<ResourceStat>>(Array.Empty<ResourceStat>()));
        var regularLease = descriptors.Acquire(regularFd);
        Assert.Throws<NamespaceFidException>(() => regularLease.Directory);
        await regularLease.DisposeAsync();
        await descriptors.CloseAsync(regularFd).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        var f = new StreamingDirectoryFixture();
        f.Union(f.Directory("A", f.Record("a", 60)), f.Directory("B"));
        DirectoryMountHead head = f.Mounts.RetainDirectoryHead(f.Root.Identity)!;
        int fd = await f.Calls.OpenAsync("/", new(0));
        await f.Calls.ReadAsync(fd, 60);
        var lease = f.Files.Process.Descriptors.Acquire(fd);
        AssertAvailable(CursorGate(lease.Directory));
        AssertAvailable(head.Gate);
        await f.Calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
        AssertAvailable(CursorGate(lease.Directory));
        await lease.DisposeAsync();
        await f.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(f.Opens.Count, f.Closes.Count);

        var resources = new MemoryDataResources();
        var root = resources.Directory("root");
        var session = new NamespaceSession("progress", 1, "user", new LocalNamespaceDataPlane(new MountTable(), resources));
        await session.AttachAsync(1, root);
        var gates = (Dictionary<uint, SemaphoreSlim>)typeof(NamespaceSession)
            .GetField("fidGates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        AssertAvailable(gates[1]);
        await session.WalkAsync(1, 2, Array.Empty<string>());
        AssertAvailable(gates[1]);
        AssertAvailable(gates[2]);

        // Fail acquisition after one gate has been acquired, then inspect its release.
        Assert.True(gates[2].Wait(0));
        using var cancellation = new CancellationTokenSource();
        Task waiting = session.WalkAsync(1, 2, Array.Empty<string>(), cancellation.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        gates[2].Release();
        AssertAvailable(gates[1]);
        AssertAvailable(gates[2]);

        // Closing admission is immediate even when a currently admitted operation
        // holds a gate. Repeated disposal must not join that same gate queue.
        Assert.True(gates[1].Wait(0));
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => session.GetFidResource(1));
        Assert.True(session.DisposeAsync().IsCompletedSuccessfully, "Concurrent session close must return immediately.");
        gates[1].Release();
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(session.DisposeAsync().IsCompletedSuccessfully, "Repeated session close must not reacquire retired gates.");
    }

    private static SemaphoreSlim CursorGate(IDirectoryCursor cursor)
        => (SemaphoreSlim)cursor.GetType().GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cursor)!;

    private static void AssertAvailable(SemaphoreSlim gate)
        => Assert.Equal(1, gate.CurrentCount);
}

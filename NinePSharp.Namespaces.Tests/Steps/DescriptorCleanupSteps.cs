using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Local descriptor cleanup")]
public sealed class DescriptorCleanupSteps
{
    private readonly List<int> attempts = new();
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private VProcessTable table = null!;
    private VProcess process = null!;
    private DescriptorGroup original = null!;
    private VProcessGroup originalNamespace = null!;
    private Task<bool>? exiting;
    private bool failFirst;
    private bool holdFirst;

    [Given("a cleanup process with two descriptors")]
    public void GivenProcess()
    {
        (table, process) = DescriptorGroupTests.CreateProcess();
        original = process.Descriptors;
        originalNamespace = process.ProcessGroup;
        original.Install(DescriptorGroupTests.Handle("marked"), () => CloseProvider(0), true);
        original.Install(DescriptorGroupTests.Handle("unmarked"), () => CloseProvider(1));
    }

    [Given("the first provider close fails")]
    public void FailFirstClose() => failFirst = true;

    [Given("the first provider close is held pending")]
    public void HoldFirstClose() => holdFirst = true;

    [When("the cleanup process terminates")]
    public async Task Terminate() => Assert.True(await table.TerminateAsync(process.Id));

    [When("process termination begins and reaches the pending close")]
    public async Task StartTermination()
    {
        exiting = table.TerminateAsync(process.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [When("the pending provider close completes")]
    public async Task FinishProviderClose()
    {
        resume.SetResult();
        Assert.True(await exiting!);
    }

    [When("it applies RFCFDG as the final owner of its old descriptor table")]
    public async Task EmptyRfork() => await table.RforkDescriptorsAsync(process.Id, DescriptorForkMode.Empty);

    [When("its close-on-exec hook runs")]
    public async Task CloseOnExec() => await original.CloseOnExecAsync();

    [When("the first descriptor is closed")]
    public async Task CloseFirst() => await original.CloseAsync(0);

    [When("termination is requested again")]
    public async Task RepeatTermination() => Assert.False(await table.TerminateAsync(process.Id));

    [Then("its old descriptor table is closed and empty")]
    public void OldTableClosed()
    {
        Assert.True(original.IsClosed);
        Assert.Equal(0, original.OwnerCount);
        Assert.Empty(original.Snapshot());
        Assert.Throws<ObjectDisposedException>(() => original.Acquire(0));
    }

    [Then("new descriptor installation is rejected without taking ownership")]
    public void RejectInstallation()
    {
        Assert.Throws<ObjectDisposedException>(() => original.Install(DescriptorGroupTests.Handle("late"), () => CloseProvider(2)));
        lock (attempts) Assert.Equal(new[] { 0 }, attempts);
    }

    [Then("both provider close callbacks have been attempted once in descriptor order")]
    public void BothClosesAttempted()
    {
        lock (attempts) Assert.Equal(new[] { 0, 1 }, attempts);
    }

    [Then("only the marked descriptor provider close has been attempted")]
    public void OnlyMarkedCloseAttempted()
    {
        lock (attempts) Assert.Equal(new[] { 0 }, attempts);
    }

    [Then("process cleanup has completed")]
    public void CleanupComplete()
    {
        Assert.True(process.IsTerminated);
        Assert.True(process.TerminationCompletion.IsCompletedSuccessfully);
    }

    [Then("process cleanup is still pending")]
    public void CleanupPending()
    {
        Assert.True(process.IsTerminated);
        Assert.False(process.TerminationCompletion.IsCompleted);
        Assert.False(exiting!.IsCompleted);
    }

    [Then("the process remains active with an empty descriptor table and the same namespace")]
    public void ProcessHasNewTable()
    {
        Assert.False(process.IsTerminated);
        Assert.NotSame(original, process.Descriptors);
        Assert.False(process.Descriptors.IsClosed);
        Assert.Equal(1, process.Descriptors.OwnerCount);
        Assert.Empty(process.Descriptors.Snapshot());
        Assert.Same(originalNamespace, process.ProcessGroup);
    }

    [Then("only the unmarked descriptor remains usable")]
    public async Task UnmarkedDescriptorRemains()
    {
        Assert.Equal(1, Assert.Single(original.Snapshot()).Number);
        Assert.Throws<ArgumentException>(() => original.Acquire(0));
        await using var lease = original.Acquire(1);
        Assert.Equal("unmarked", lease.Handle.HandleId);
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        resume.TrySetResult();
        if (exiting is not null) await exiting;
        if (table is not null)
            foreach (var remaining in table.Snapshot()) await table.TerminateAsync(remaining.Id);
    }

    private async ValueTask CloseProvider(int descriptor)
    {
        lock (attempts) attempts.Add(descriptor);
        if (descriptor == 0)
        {
            entered.TrySetResult();
            if (holdFirst) await resume.Task;
            if (failFirst) throw new IOException("Provider close failed.");
        }
    }
}

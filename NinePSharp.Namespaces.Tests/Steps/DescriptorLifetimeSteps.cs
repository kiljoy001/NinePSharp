using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Local process descriptor lifetimes")]
public sealed class DescriptorLifetimeSteps
{
    private VProcessTable table = null!;
    private VProcess parent = null!;
    private VProcess child = null!;
    private DescriptorLease? lease;
    private int closed;

    [Given("a parent process with an open descriptor")]
    public void GivenParent()
    {
        (table, parent) = DescriptorGroupTests.CreateProcess();
        parent.Descriptors.Install(DescriptorGroupTests.Handle("open-instance"), () =>
        {
            Interlocked.Increment(ref closed);
            return ValueTask.CompletedTask;
        });
    }

    [When("a child receives a (.*) namespace and a (.*) descriptor table")]
    public void ForkChild(NamespaceForkMode ns, DescriptorForkMode descriptors)
        => child = table.Fork(parent.Id, ns, descriptorMode: descriptors);

    [When("the parent closes its descriptor")]
    public async Task CloseParentDescriptor() => await parent.Descriptors.CloseAsync(0);

    [When("the parent terminates")]
    public async Task ParentExits() => Assert.True(await table.TerminateAsync(parent.Id));

    [Then("the child has (.*) descriptors")]
    public void ChildDescriptorCount(int expected)
        => Assert.Equal(expected, child.Descriptors.Snapshot().Count);

    [Then("the provider has been closed (.*) times")]
    public void ProviderCloseCount(int expected) => Assert.Equal(expected, closed);

    [Given("an operation retaining that descriptor channel")]
    public void RetainOperation() => lease = parent.Descriptors.Acquire(0);

    [When("the admitted operation releases its channel")]
    public async Task ReleaseOperation()
    {
        Assert.Equal("open-instance", lease!.Handle.HandleId);
        await lease.DisposeAsync();
    }

    [When("all descriptor owners terminate")]
    public async Task AllExit()
    {
        foreach (var process in table.Snapshot()) await table.TerminateAsync(process.Id);
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        if (table is not null) await AllExit();
        if (lease is not null) await lease.DisposeAsync();
    }
}

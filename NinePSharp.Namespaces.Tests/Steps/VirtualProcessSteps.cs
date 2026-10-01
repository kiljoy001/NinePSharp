using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Virtual processes own Plan 9 process-group namespaces")]
public sealed class VirtualProcessSteps
{
    private readonly MemoryResources resources = new();
    private readonly VProcessTable processes = new();
    private ResourceHandle? mountedOn;
    private ResourceHandle? firstTarget;
    private ResourceHandle? secondTarget;
    private VProcess? parent;
    private VProcess? child;
    private VProcessGroup? retainedGroup;

    [When("the parent terminates")]
    public void ParentTerminates()
    {
        retainedGroup = parent!.ProcessGroup;
        Assert.True(processes.Terminate(parent.Id));
    }

    [When("the child terminates")]
    public void ChildTerminates() => Assert.True(processes.Terminate(child!.Id));

    [Then("the shared namespace has one owner")]
    public void OneOwner() => Assert.Equal(1, retainedGroup!.OwnerCount);

    [Then("the shared namespace is closed and empty")]
    public void ClosedAndEmpty()
    {
        Assert.True(retainedGroup!.MountTable.IsClosed);
        Assert.Empty(retainedGroup.MountTable.Snapshot().MountHeads);
        Assert.Empty(processes.Snapshot());
    }

    [Given("a parent virtual process")]
    public void GivenParent()
    {
        ResourceHandle root = resources.Directory("root");
        mountedOn = resources.Directory("mountpoint");
        firstTarget = resources.Directory("first");
        secondTarget = resources.Directory("second");
        parent = processes.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
    }

    [Given("a parent virtual process with an existing mount")]
    public void GivenParentWithMount()
    {
        GivenParent();
        parent!.ProcessGroup.MountTable.Mount(firstTarget!, mountedOn!);
    }

    [Given("a child forked with a shared namespace")]
    public void GivenSharedChild() => child = processes.Fork(parent!.Id, NamespaceForkMode.Share);

    [Given("a child forked with a copied namespace")]
    public void GivenCopiedChild() => child = processes.Fork(parent!.Id, NamespaceForkMode.Copy);

    [Given("a child forked with an empty namespace")]
    public void GivenEmptyChild() => child = processes.Fork(parent!.Id, NamespaceForkMode.Empty);

    [When("the parent mounts a replacement")]
    public void ParentMountsReplacement()
        => parent!.ProcessGroup.MountTable.Mount(firstTarget!, mountedOn!);

    [When("the parent replaces its existing mount")]
    public void ParentReplacesMount()
        => parent!.ProcessGroup.MountTable.Mount(secondTarget!, mountedOn!);

    [Then("the child observes the replacement")]
    public void ChildObservesReplacement()
        => Assert.Equal(
            firstTarget!.Identity,
            child!.ProcessGroup.MountTable.Find(mountedOn!.Identity)!.Mounts[0].Target.Identity);

    [Then("the child retains the original mounted resource")]
    public void ChildRetainsOriginal()
        => Assert.Equal(
            firstTarget!.Identity,
            child!.ProcessGroup.MountTable.Find(mountedOn!.Identity)!.Mounts[0].Target.Identity);

    [Then("the child has a different process group")]
    public void ChildHasDifferentGroup() => Assert.NotEqual(parent!.ProcessGroup.Id, child!.ProcessGroup.Id);

    [Then("the child has no mount at that resource")]
    public void ChildHasNoMount() => Assert.Null(child!.ProcessGroup.MountTable.Find(mountedOn!.Identity));
}

using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Namespace syscalls resolve union paths")]
public sealed class NamespaceSyscallPathSteps
{
    private readonly MemoryResources resources = new();
    private VProcess process = null!;
    private ResourceHandle union = null!;
    private ResourceHandle target = null!;
    private ResourceHandle source = null!;
    private ResourceHandle first = null!;

    [Given("a mount point found only in the second union directory")]
    public void GivenUnion()
    {
        ResourceHandle root = resources.Directory("root");
        union = resources.AddChild(root, "union", true);
        target = resources.AddChild(union, "target", true);
        source = resources.AddChild(root, "source", true);
        first = resources.Directory("first");
        process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        process.ProcessGroup.MountTable.Mount(first, union, MountFlags.Before);
        process.ProcessGroup.MountTable.Mount(resources.Directory("hidden"), target);
    }

    [When("I {word} through the union path")]
    public async Task Perform(string operation)
    {
        var syscalls = new NamespaceSyscalls(resources);
        switch (operation)
        {
            case "bind":
                await syscalls.BindAsync(process, "/source", "/union/target");
                break;
            case "mount":
                var service = new NamespaceMountSource(
                    new NamespaceNavigator(new MountTable(), resources).Attach(source), NinePConstants.ORDWR);
                await syscalls.MountAsync(process, service, "/union/target");
                break;
            case "unmount":
                await syscalls.UnmountAsync(process, "/union/target");
                break;
            default:
                throw new ArgumentException("Unknown scenario operation", nameof(operation));
        }
    }

    [Then("the underlying mount point is {word}")]
    public void CheckTarget(string result)
    {
        MountHead? head = process.ProcessGroup.MountTable.Find(target.Identity);
        if (result == "unmounted")
        {
            Assert.Null(head);
        }
        else
        {
            Assert.Equal(source.Identity, Assert.Single(Assert.IsType<MountHead>(head).Mounts).Target.Identity);
        }
    }

    [Then("the parent union remains intact")]
    public void CheckUnion()
    {
        MountHead head = process.ProcessGroup.MountTable.Find(union.Identity)!;
        Assert.Equal(new[] { first.Identity, union.Identity }, head.Mounts.Select(binding => binding.Target.Identity));
    }
}

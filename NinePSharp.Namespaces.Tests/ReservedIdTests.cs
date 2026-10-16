using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class ReservedIdTests
{
    [Fact]
    public void AReservedIdIsNoProcessAndIsNeverGivenToOne()
    {
        var resources = new MemoryResources();
        var table = new VProcessTable();
        VProcess first = table.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(resources.Directory("root")));

        long reserved = table.ReserveId();
        VProcess child = table.Fork(first.Id, NamespaceForkMode.Share);

        Assert.Equal((first.Id + 1, first.Id + 2), (reserved, child.Id));
        Assert.Throws<KeyNotFoundException>(() => table.Get(reserved));
    }
}

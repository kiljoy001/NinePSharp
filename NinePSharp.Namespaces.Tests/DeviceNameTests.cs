using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

// namec's #: with an attach, a name starting with # begins at the device its rune names, attached
// with the spec up to the first slash; without one, such a name is walked from dot as any other.
public sealed class DeviceNameTests
{
    [Fact]
    public async Task ADeviceNameOpensTheFileUnderTheDeviceItNames()
    {
        await using var f = new FileSyscallFixture();
        var attached = new List<(string Device, string Spec)>();
        ResourceHandle device = f.Resources.Directory("dev", "user");
        var calls = new Plan9FileSyscalls(f.Process, f.Plane, f.Context, attach: (rune, spec) =>
        {
            attached.Add((rune, spec));
            return device;
        });

        int fd = await calls.OpenAsync("#c/user", new(NinePConstants.OREAD));
        await calls.OpenAsync("#cspec", new(NinePConstants.OREAD));
        await calls.OpenAsync("#¤/user", new(NinePConstants.OREAD));
        await calls.OpenAsync("#/", new(NinePConstants.OREAD));
        await calls.OpenAsync("#//user", new(NinePConstants.OREAD));

        Assert.True(fd >= 0);
        Assert.Equal([("c", string.Empty), ("c", "spec"), ("¤", string.Empty), ("/", string.Empty), ("/", string.Empty)], attached);
    }

    [Fact]
    public async Task ADeviceNameCreatesAndStatsFilesUnderTheDevice()
    {
        await using var f = new FileStatFixture();
        ResourceHandle device = f.Files.Resources.Directory("mounted");
        var calls = new Plan9FileSyscalls(f.Files.Process, f.Files.Plane, f.Files.Context, fileStats: f, attach: (_, _) => device);

        await calls.CreateAsync("#e/home", new(0b110_110_100, NinePConstants.OWRITE));
        await calls.StatAsync("#e/home", 1024);

        ResourceHandle? home = await f.Files.Resources.WalkAsync(device, "home", default);
        Assert.Equal(home, f.Requests[^1].Resource);
    }

    [Fact]
    public async Task WithoutAnAttachADeviceNameIsANameUnderDot()
    {
        await using var f = new FileSyscallFixture();
        await Assert.ThrowsAsync<NamespaceException>(() => f.Calls.OpenAsync("#c/user", new(NinePConstants.OREAD)).AsTask());
    }

    [Fact]
    public async Task TheAttachDecidesWhichDevicesThereAreAndALoneHashNamesNone()
    {
        await using var f = new FileSyscallFixture();
        string? asked = null;
        var calls = new Plan9FileSyscalls(f.Process, f.Plane, f.Context, attach: (rune, spec) =>
        {
            asked = $"{rune}|{spec}";
            throw new IOException("unknown device in # filename");
        });

        Assert.Equal("unknown device in # filename", (await Assert.ThrowsAsync<IOException>(() => calls.OpenAsync("#", new(NinePConstants.OREAD)).AsTask())).Message);
        Assert.Equal("|", asked);
    }

    [Fact]
    public async Task ADeviceNameBindsTheDeviceOnAPath()
    {
        var resources = new MemoryResources();
        ResourceHandle root = resources.Directory("root");
        resources.AddChild(root, "dev", true);
        ResourceHandle device = resources.Directory("cons", "user");
        VProcess process = new VProcessTable().CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(root));
        var syscalls = new NamespaceSyscalls(resources, (_, _) => device);

        await syscalls.BindAsync(process, "#c", "/dev");

        NamespaceWalkResult walked = await new NamespaceNavigator(process.ProcessGroup.MountTable, resources).WalkAsync(process.Root.Clone(), ["dev", "user"]);
        Assert.True(walked.Complete(2));
    }
}

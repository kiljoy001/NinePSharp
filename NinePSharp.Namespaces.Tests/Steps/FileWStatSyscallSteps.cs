using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "Processes change native file metadata atomically")]
public sealed class FileWStatSyscallSteps
{
    private readonly FileStatFixture f = new();
    private byte[] update = Array.Empty<byte>();
    private string path = string.Empty;
    private int descriptor;
    private Exception? error;
    private uint result;

    [Given("a malformed wstat record and an absent path")]
    public void Malformed()
    {
        update = new byte[48];
        path = "/absent";
    }

    [When("wstat is attempted on that path")]
    public async Task AttemptPath()
    {
        try
        {
            await f.Calls.WStatAsync(path, update);
        }
        catch (Exception caught)
        {
            error = caught;
        }
    }

    [Then("bad stat wins and no metadata update reaches a provider")]
    public void BadStatWins()
    {
        Assert.IsType<NamespaceFidException>(error);
        Assert.Empty(f.Updates);
    }

    [Given("a valid all-sentinel wstat record")]
    public void AllSentinel() => update = Encode(value => value);

    [When("fwstat submits it to an open file whose provider returns 73")]
    public async Task SubmitSentinel()
    {
        f.UpdateReply = (_, _, _) => ValueTask.FromResult(73U);
        descriptor = await f.Calls.OpenAsync("/file", new(0));
        result = await f.Calls.FWStatAsync(descriptor, update);
    }

    [Then("fwstat returns 73 and keeps the descriptor installed")]
    public void DeviceResult()
    {
        Assert.Equal(73U, result);
        Assert.Contains(f.Files.Process.Descriptors.Snapshot(), slot => slot.Number == descriptor);
        Assert.Single(f.Updates);
    }

    [Given("a directory descriptor opened on a mount point")]
    public async Task OpenMountPoint()
    {
        ResourceHandle root = f.Files.Process.Root.Current;
        ResourceHandle mountedOn = await f.Files.Resources.CreateAsync(root, "dir", true, default);
        ResourceHandle target = f.Files.Resources.Directory("bdd-mounted-wstat");
        f.Files.Process.ProcessGroup.MountTable.Mount(target, mountedOn);
        descriptor = await f.Calls.OpenAsync("/dir", new(0));
        f.Files.Process.ProcessGroup.MountTable.Unmount(mountedOn);
    }

    [When("that mount is removed and fwstat requests a nonempty name")]
    public async Task RenameMountPoint()
    {
        try
        {
            await f.Calls.FWStatAsync(descriptor, Encode(value => value with { Name = "renamed" }));
        }
        catch (Exception caught)
        {
            error = caught;
        }
    }

    [Then("fwstat rejects the mount-point rename before provider dispatch")]
    public void MountPointRejected()
    {
        Assert.IsType<NamespaceFidException>(error);
        Assert.Empty(f.Updates);
    }

    [Given("an open file containing twenty bytes")]
    public async Task OpenFile()
    {
        descriptor = await f.Calls.OpenAsync("/file", new(NinePConstants.ORDWR));
        await f.Calls.PWriteAsync(descriptor, 0, new byte[20]);
    }

    [When("fwstat renames it to final and changes mode to 0600 and length to 3")]
    public async Task CombinedUpdate()
        => result = await f.Calls.FWStatAsync(
            descriptor,
            Encode(value => value with { Name = "final", Mode = NinePConstants.Mode0600, Length = 3 }));

    [Then("the provider exposes all three changes together")]
    public async Task CombinedResult()
    {
        ResourceHandle renamed = (await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "final", default))!;
        ResourceStat stat = await f.Files.Resources.StatAsync(renamed, default);
        Assert.Null(await f.Files.Resources.WalkAsync(f.Files.Process.Root.Current, "file", default));
        Assert.Equal(NinePConstants.Mode0600, stat.Mode);
        Assert.Equal(3UL, stat.Length);
        Assert.Equal((uint)update.Length, result);
    }

    [AfterScenario]
    public async Task Cleanup() => await f.DisposeAsync();

    private byte[] Encode(Func<ResourceWStat, ResourceWStat> change)
    {
        update = FileStatOperations.EncodeUpdate(change(ResourceWStat.Unchanged()));
        return update;
    }
}

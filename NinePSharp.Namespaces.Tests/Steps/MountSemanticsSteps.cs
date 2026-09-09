using NinePSharp.Namespaces.Tests.Support;
using Reqnroll;
using Xunit;

namespace NinePSharp.Namespaces.Tests.Steps;

[Binding]
[Scope(Feature = "A namespace composed from mounted resources")]
public sealed class MountSemanticsSteps
{
    private readonly MemoryResources resources = new();
    private readonly MountTable mounts = new();
    private readonly Dictionary<string, ResourceHandle> handles = new(StringComparer.Ordinal);
    private NamespaceChannel? channel;
    private NamespaceWalkResult? walk;
    private IReadOnlyList<ResourceDirectoryEntry>? listing;
    private Exception? mountFailure;

    [Given("a directory named {word} containing {word}")]
    public void GivenDirectoryContaining(string directory, string child)
        => handles[directory] = resources.Directory(directory, child);

    [Given("a regular file named {word}")]
    public void GivenRegularFile(string name) => handles[name] = resources.File(name);

    [Given("a root containing a directory named {word}")]
    public void GivenRootContainingDirectory(string name)
    {
        ResourceHandle root = resources.Directory("root");
        handles["root"] = root;
        handles[name] = resources.AddChild(root, name, true);
    }

    [Given("{word} is mounted over {word}")]
    [When("{word} is mounted over {word}")]
    public void MountReplacement(string replacement, string original)
        => TryMount(replacement, original, MountFlags.Replace);

    [Given("{word} is mounted before {word}")]
    [When("{word} is mounted before {word}")]
    public void MountBefore(string replacement, string original)
        => TryMount(replacement, original, MountFlags.Before);

    [When("{word} is mounted before {word} and permits creation")]
    public void MountBeforeCreatable(string replacement, string original)
        => TryMount(replacement, original, MountFlags.Before | MountFlags.Create);

    [When("the mounted {word} channel is bound over {word}")]
    public void BindMountedChannel(string source, string destination)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        channel = navigator.Attach(handles[source]);
        mountFailure = Record.Exception(() => navigator.Mount(channel, handles[destination]));
    }

    [When("{word} is unmounted from {word}")]
    public void UnmountMember(string mounted, string mountedOn)
        => mountFailure = Record.Exception(() => mounts.Unmount(handles[mountedOn], handles[mounted]));

    [Given("a channel is open on {word}")]
    public void OpenChannel(string name)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        channel = navigator.Attach(handles[name]);
    }

    [When("a file named {word} is created through {word}")]
    public async Task CreateThrough(string child, string original)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        channel = navigator.Attach(handles[original]);
        await navigator.CreateAsync(channel, child, false);
    }

    [When("{word} is walked through {word} and then dot dot is walked twice")]
    public async Task WalkThroughAndBack(string child, string mountpoint)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        channel = navigator.Attach(handles["root"]);
        walk = await navigator.WalkAsync(channel, new[] { mountpoint, child, "..", ".." });
    }

    [Then("walking {word} from {word} reaches {word}")]
    public async Task WalkReaches(string child, string start, string expectedDevice)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        NamespaceWalkResult result = await navigator.WalkAsync(navigator.Attach(handles[start]), new[] { child });
        Assert.Single(result.Qids);
        Assert.Equal(expectedDevice, result.Channel.Current.Identity.Device);
    }

    [Then("walking {word} from {word} fails")]
    public async Task WalkFails(string child, string start)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        NamespaceWalkResult result = await navigator.WalkAsync(navigator.Attach(handles[start]), new[] { child });
        Assert.Empty(result.Qids);
    }

    [Then("reading {word} lists {word} before {word}")]
    public async Task ReadListsInOrder(string start, string first, string second)
    {
        var navigator = new NamespaceNavigator(mounts, resources);
        listing = await navigator.ReadDirectoryAsync(navigator.Attach(handles[start]));
        Assert.Equal(new[] { first, second }, listing.Select(entry => entry.Name));
    }

    [Then("reading the open channel lists {word} before {word} before {word}")]
    public async Task ReadOpenChannelListsInOrder(string first, string second, string third)
    {
        Assert.NotNull(channel);
        var navigator = new NamespaceNavigator(mounts, resources);
        listing = await navigator.ReadDirectoryAsync(channel);
        Assert.Equal(new[] { first, second, third }, listing.Select(entry => entry.Name));
    }

    [Then("{word} contains {word}")]
    public void Contains(string directory, string child) => Assert.True(resources.Contains(handles[directory], child));

    [Then("{word} does not contain {word}")]
    public void DoesNotContain(string directory, string child) => Assert.False(resources.Contains(handles[directory], child));

    [Then("the mount is rejected because unions require directories")]
    public void MountRejected()
    {
        NamespaceException failure = Assert.IsType<NamespaceException>(mountFailure);
        Assert.Equal(NamespaceError.UnionRequiresDirectory, failure.Error);
    }

    [Then("the channel is back at the root")]
    public void BackAtRoot()
    {
        Assert.NotNull(walk);
        Assert.Equal(handles["root"].Identity, walk.Channel.Current.Identity);
        Assert.Empty(walk.Channel.VisiblePath);
    }

    private void TryMount(string replacement, string original, MountFlags flags)
    {
        mountFailure = Record.Exception(() => mounts.Mount(handles[replacement], handles[original], flags));
    }
}

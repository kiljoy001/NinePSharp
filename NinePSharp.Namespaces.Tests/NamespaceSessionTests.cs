using System.Text;
using NinePSharp.Constants;
using NinePSharp.Namespaces.Tests.Support;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class NamespaceSessionTests
{
    [Fact]
    public async Task ConstructorValidatesAndRetainsIdentity()
    {
        var resources = new MemoryDataResources();
        var dataPlane = new LocalNamespaceDataPlane(new MountTable(), resources);

        Assert.Throws<ArgumentException>(() => new NamespaceSession("", 7, "glenda", dataPlane));
        Assert.Throws<ArgumentException>(() => new NamespaceSession("session", 7, "", dataPlane));
        Assert.Throws<ArgumentNullException>(() => new NamespaceSession("session", 7, "glenda", null!));

        await using var session = new NamespaceSession("session", 7, "glenda", dataPlane);
        Assert.Equal("session", session.SessionId);
        Assert.Equal(7, session.ProcessId);
        Assert.Equal("glenda", session.User);
    }

    [Fact]
    public async Task AttachRejectsDuplicateFidAndOperationsAfterDispose()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.AttachAsync(1, test.Root).AsTask());
        Assert.True(test.Session.ContainsFid(1));

        await test.Session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => test.Session.AttachAsync(2, test.Root).AsTask());
        await test.Session.DisposeAsync();
    }

    [Fact]
    public async Task AttachHonorsAnAlreadyCanceledToken()
    {
        await using TestSession test = CreateSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => test.Session.AttachAsync(1, test.Root, cancellation.Token).AsTask());
        Assert.False(test.Session.ContainsFid(1));
    }

    [Fact]
    public async Task EmptyWalkClonesFidAndDuplicateTargetIsRejected()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);

        NamespaceWalkResult clone = await test.Session.WalkAsync(1, 2, Array.Empty<string>());

        Assert.Empty(clone.Qids);
        Assert.True(test.Session.ContainsFid(1));
        Assert.True(test.Session.ContainsFid(2));
        Assert.Equal(test.Root, test.Session.GetFidResource(2));
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.WalkAsync(1, 2, Array.Empty<string>()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => test.Session.WalkAsync(1, 3, null!).AsTask());
    }

    [Fact]
    public async Task WalkOnSameFidReplacesItOnlyAfterCompleteWalk()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);

        NamespaceWalkResult partial = await test.Session.WalkAsync(1, 1, new[] { "missing" });
        Assert.False(partial.Complete(1));
        Assert.Equal(test.Root, test.Session.GetFidResource(1));

        NamespaceWalkResult result = await test.Session.WalkAsync(1, 1, new[] { "child" });
        Assert.True(result.Complete(1));
        Assert.False(test.Session.GetFidResource(1).IsDirectory);
    }

    [Fact]
    public async Task NonDirectoryCannotBeWalkedWithNames()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        NamespaceFidException error = await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.WalkAsync(2, 3, new[] { "grandchild" }).AsTask());

        Assert.Contains("non-directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyWalkCanCloneANonDirectory()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        NamespaceWalkResult clone = await test.Session.WalkAsync(2, 3, Array.Empty<string>());

        Assert.Empty(clone.Qids);
        Assert.Equal(test.Session.GetFidResource(2), test.Session.GetFidResource(3));
    }

    [Fact]
    public async Task OpenRejectsUnknownFlagsSecondOpenAndDirectoryWrite()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.OpenAsync(1, 0x80).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.OpenAsync(1, NinePConstants.OWRITE).AsTask());
        await test.Session.OpenAsync(1, NinePConstants.OREAD | NinePConstants.ORCLOSE);
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.OpenAsync(1, NinePConstants.OREAD).AsTask());
    }

    [Fact]
    public async Task DirectoryRequirementIsCheckedBeforeOpening()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.OpenAsync(2, NinePConstants.OREAD, requireDirectory: true).AsTask());
        await test.Session.OpenAsync(2, NinePConstants.OREAD);
        Assert.True(test.Session.ContainsFid(2));
    }

    [Fact]
    public async Task OpenRefreshesTheFidQid()
    {
        await using TestSession test = CreateSession("child");
        test.Resources.AdvanceVersionOnOpen = true;
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        await test.Session.OpenAsync(2, NinePConstants.OREAD);

        Assert.Equal(1U, test.Session.GetFidResource(2).Version);
    }

    [Fact]
    public async Task ReadAndWriteEnforceOpenAccessMode()
    {
        await using TestSession test = CreateSession("readable", "writable");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "readable" });
        await test.Session.WalkAsync(1, 3, new[] { "writable" });

        await Assert.ThrowsAsync<NamespaceFidException>(() => test.Session.ReadAsync(2, 0, 1).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.WriteAsync(3, 0, ReadOnlyMemory<byte>.Empty).AsTask());

        await test.Session.OpenAsync(2, NinePConstants.OWRITE);
        await test.Session.OpenAsync(3, NinePConstants.OREAD);
        await Assert.ThrowsAsync<NamespaceFidException>(() => test.Session.ReadAsync(2, 0, 1).AsTask());
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.WriteAsync(3, 0, ReadOnlyMemory<byte>.Empty).AsTask());
    }

    [Theory]
    [InlineData(NinePConstants.OREAD)]
    [InlineData(NinePConstants.ORDWR)]
    [InlineData(NinePConstants.OEXEC)]
    public async Task ReadableModesCanRead(byte mode)
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });
        await test.Session.OpenAsync(2, mode);

        Assert.Empty((await test.Session.ReadAsync(2, 0, 1)).ToArray());
    }

    [Theory]
    [InlineData(NinePConstants.OWRITE)]
    [InlineData(NinePConstants.ORDWR)]
    public async Task WritableModesCanWrite(byte mode)
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });
        await test.Session.OpenAsync(2, mode);

        Assert.Equal(3U, await test.Session.WriteAsync(2, 0, Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public async Task DirectoryReadRequiresAnOpenDirectory()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.ReadDirectoryAsync(1).AsTask());

        await test.Session.OpenAsync(1, NinePConstants.OREAD);
        IReadOnlyList<ResourceStat> entries = await test.Session.ReadDirectoryAsync(1);

        Assert.Single(entries);
        Assert.Equal("child", entries[0].Name);
    }

    [Fact]
    public async Task OpenFileCannotBeReadAsDirectory()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });
        await test.Session.OpenAsync(2, NinePConstants.OREAD);

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.ReadDirectoryAsync(2).AsTask());
    }

    [Fact]
    public async Task DirectoryReadChecksAccessBeforeResourceType()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });
        await test.Session.OpenAsync(2, NinePConstants.OWRITE);

        NamespaceFidException error = await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.ReadDirectoryAsync(2).AsTask());

        Assert.Contains("not open for reading", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    public async Task CreateRejectsInvalidNames(string name)
    {
        await using TestSession test = CreateSession();
        await test.Session.AttachAsync(1, test.Root);

        Exception error = await Record.ExceptionAsync(
            () => test.Session.CreateAsync(1, name, NinePConstants.Mode0644, NinePConstants.OREAD).AsTask());
        if (name.Length == 0)
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.IsType<NamespaceFidException>(error);
        }

        Assert.Equal(test.Root, test.Session.GetFidResource(1));
    }

    [Fact]
    public async Task CreateRejectsOpenAndNonDirectoryFids()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.CreateAsync(2, "nested", NinePConstants.Mode0644, NinePConstants.OREAD).AsTask());

        await test.Session.OpenAsync(1, NinePConstants.OREAD);
        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.CreateAsync(1, "nested", NinePConstants.Mode0644, NinePConstants.OREAD).AsTask());
    }

    [Fact]
    public async Task DirectoryCreateRejectsWriteOpenMode()
    {
        await using TestSession test = CreateSession();
        await test.Session.AttachAsync(1, test.Root);

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.CreateAsync(
                1,
                "directory",
                (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755,
                NinePConstants.OWRITE).AsTask());

        Assert.Equal(test.Root, test.Session.GetFidResource(1));
    }

    [Fact]
    public async Task FileCreateRejectsUnknownOpenFlagsBeforeProviderMutation()
    {
        await using TestSession test = CreateSession();
        await test.Session.AttachAsync(1, test.Root);

        await Assert.ThrowsAsync<NamespaceFidException>(
            () => test.Session.CreateAsync(1, "file", NinePConstants.Mode0644, 0x80).AsTask());
        NamespaceWalkResult missing = await test.Session.WalkAsync(1, 2, new[] { "file" });

        Assert.Empty(missing.Qids);
        Assert.False(test.Session.ContainsFid(2));
    }

    [Fact]
    public async Task ClunkOfUnopenedFidDoesNotCallProviderAndRemoveAlwaysInvalidates()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        await test.Session.ClunkAsync(1);
        Assert.Equal(0, test.Resources.ClunkCount);
        await test.Session.RemoveAsync(2);
        Assert.False(test.Session.ContainsFid(1));
        Assert.False(test.Session.ContainsFid(2));
        await Assert.ThrowsAsync<NamespaceFidException>(() => test.Session.StatAsync(2).AsTask());
    }

    [Fact]
    public async Task RemoveDeletesTheProviderResource()
    {
        await using TestSession test = CreateSession("child");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "child" });

        await test.Session.RemoveAsync(2);
        NamespaceWalkResult missing = await test.Session.WalkAsync(1, 3, new[] { "child" });

        Assert.Empty(missing.Qids);
        Assert.False(test.Session.ContainsFid(3));
    }

    [Fact]
    public async Task DisposeClunksEveryOpenFidAndRejectsFurtherAccess()
    {
        await using TestSession test = CreateSession("one", "two");
        await test.Session.AttachAsync(1, test.Root);
        await test.Session.WalkAsync(1, 2, new[] { "one" });
        await test.Session.WalkAsync(1, 3, new[] { "two" });
        await test.Session.OpenAsync(2, NinePConstants.OREAD);
        await test.Session.OpenAsync(3, NinePConstants.OREAD);

        await test.Session.DisposeAsync();

        Assert.Equal(2, test.Resources.ClunkCount);
        Assert.False(test.Session.ContainsFid(1));
        Assert.False(test.Session.ContainsFid(2));
        Assert.False(test.Session.ContainsFid(3));
        Assert.Throws<ObjectDisposedException>(() => test.Session.GetFidResource(1));
    }

    private static TestSession CreateSession(params string[] children)
    {
        var resources = new MemoryDataResources();
        ResourceHandle root = resources.Directory(Guid.NewGuid().ToString("N"), children);
        return new TestSession(resources, root);
    }

    private sealed class TestSession : IAsyncDisposable
    {
        internal TestSession(MemoryDataResources resources, ResourceHandle root)
        {
            Resources = resources;
            Root = root;
            Session = new NamespaceSession(
                "session",
                7,
                "glenda",
                new LocalNamespaceDataPlane(new MountTable(), resources));
        }

        internal MemoryDataResources Resources { get; }

        internal ResourceHandle Root { get; }

        internal NamespaceSession Session { get; }

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}

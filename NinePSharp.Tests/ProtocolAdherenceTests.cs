using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

public sealed class ProtocolAdherenceTests
{
    [Fact]
    public async Task Walk_First_Element_Failure_Returns_Error_And_Does_Not_Create_NewFid()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/tree", () => new ExistingPathFileSystem(new[] { "/valid" }))
        });

        await DispatcherIntegrationTestKit.AttachAsync(dispatcher, 1, 1, "tree");

        var walkResponse = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "missing" })),
            NinePDialect.NineP2000);

        walkResponse.Should().BeOfType<Rerror>();

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 1);
        var sourceRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 1, 0, 64);
        DispatcherIntegrationTestKit.ReadPayload(sourceRead).Should().Be("/");

        var openMissing = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTopen(new Topen(5, 2, NinePConstants.OREAD)),
            NinePDialect.NineP2000);

        openMissing.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Walk_Partial_Success_Binds_NewFid_To_Deepest_Successful_Element_Without_Mutating_Source()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/tree", () => new ExistingPathFileSystem(new[] { "/valid" }))
        });

        await DispatcherIntegrationTestKit.AttachAsync(dispatcher, 1, 1, "tree");

        var walk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 2, new[] { "valid", "missing" });
        walk.Wqid.Should().HaveCount(1);

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 2);
        var targetRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 2, 0, 64);
        DispatcherIntegrationTestKit.ReadPayload(targetRead).Should().Be("/valid");

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 5, 1);
        var sourceRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 6, 1, 0, 64);
        DispatcherIntegrationTestKit.ReadPayload(sourceRead).Should().Be("/");
    }

    [Fact]
    public async Task Walk_Nwname0_Clones_Current_Fid_State()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/tree", () => new ExistingPathFileSystem(new[] { "/valid" }))
        });

        await DispatcherIntegrationTestKit.AttachAsync(dispatcher, 1, 1, "tree");
        var initialWalk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 1, new[] { "valid" });
        initialWalk.Wqid.Should().HaveCount(1);

        var cloneWalk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 3, 1, 2, Array.Empty<string>());
        (cloneWalk.Wqid == null || cloneWalk.Wqid.Length == 0).Should().BeTrue();

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 4, 1);
        var sourceRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 5, 1, 0, 64);

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 6, 2);
        var cloneRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 7, 2, 0, 64);

        DispatcherIntegrationTestKit.ReadPayload(sourceRead).Should().Be("/valid");
        DispatcherIntegrationTestKit.ReadPayload(cloneRead).Should().Be("/valid");
    }

    [Fact]
    public async Task Treaddir_Requires_Open_Then_Succeeds()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/alpha", () => new MarkerFileSystem("alpha"))
        });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 1);

        var beforeOpen = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTreaddir(new Treaddir(24, 2, 1, 0, 256)),
            NinePDialect.NineP2000);

        beforeOpen.Should().BeOfType<Rerror>();

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 1);
        var afterOpen = await DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 4, 1, 0, 256);
        afterOpen.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Remove_On_MountPoint_Removes_Only_The_Most_Recent_Binding()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/test", () => new MarkerFileSystem("first")),
            new StubBackend("/test", () => new MarkerFileSystem("second"))
        });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 1);

        var firstWalk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 2, new[] { "test" });
        firstWalk.Wqid.Should().HaveCount(1);

        var firstRemove = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTremove(new Tremove(3, 2)),
            NinePDialect.NineP2000);
        firstRemove.Should().BeOfType<Rremove>();

        var secondWalk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 4, 1, 3, new[] { "test" });
        secondWalk.Wqid.Should().HaveCount(1);

        var secondRemove = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTremove(new Tremove(5, 3)),
            NinePDialect.NineP2000);
        secondRemove.Should().BeOfType<Rremove>();

        var finalWalk = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwalk(new Twalk(6, 1, 4, new[] { "test" })),
            NinePDialect.NineP2000);

        finalWalk.Should().BeOfType<Rerror>();
    }

    [Property(MaxTest = 40)]
    public bool Partial_Walk_Binds_NewFid_To_Last_Successful_Prefix(string[] rawSegments)
    {
        var segments = CleanSegments(rawSegments);
        if (segments.Count == 0)
        {
            return true;
        }

        var existingPaths = new List<string>();
        for (int i = 0; i < segments.Count; i++)
        {
            existingPaths.Add("/" + string.Join("/", segments.Take(i + 1)));
        }

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/tree", () => new ExistingPathFileSystem(existingPaths))
        });

        DispatcherIntegrationTestKit.AttachAsync(dispatcher, 1, 1, "tree").Sync();

        var walkNames = segments.Concat(new[] { "missing" }).ToArray();
        var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 1, 2, walkNames).Sync();

        DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 2).Sync();
        var targetRead = DispatcherIntegrationTestKit.ReadAsync(dispatcher, 4, 2, 0, 256).Sync();

        DispatcherIntegrationTestKit.OpenAsync(dispatcher, 5, 1).Sync();
        var sourceRead = DispatcherIntegrationTestKit.ReadAsync(dispatcher, 6, 1, 0, 256).Sync();

        return walk.Wqid?.Length == segments.Count
            && DispatcherIntegrationTestKit.ReadPayload(targetRead) == "/" + string.Join("/", segments)
            && DispatcherIntegrationTestKit.ReadPayload(sourceRead) == "/";
    }

    private static List<string> CleanSegments(IEnumerable<string?> rawSegments)
    {
        return rawSegments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select((segment, index) => CleanSegment(segment, $"p{index}"))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();
    }

    private static string CleanSegment(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var chars = raw.Where(char.IsLetterOrDigit).Take(12).ToArray();
        return chars.Length == 0 ? fallback : new string(chars);
    }
}

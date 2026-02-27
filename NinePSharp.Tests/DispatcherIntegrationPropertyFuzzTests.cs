using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

public class DispatcherIntegrationPropertyFuzzTests
{
    [Fact]
    public async Task Dispatcher_Namespace_Walk_BackToRoot_Then_Enter_SecondBackend_Without_Clunk()
    {
        const string alphaMarker = "marker:alpha";
        const string betaMarker = "marker:beta";

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/alpha", () => new MarkerFileSystem(alphaMarker)),
            new StubBackend("/beta", () => new MarkerFileSystem(betaMarker))
        });

        const uint rootFid = 100;
        const uint workFid = 101;

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: rootFid, newFid: workFid, wname: new[] { "alpha" });

        var alphaRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 3, fid: workFid, offset: 0, count: 128);
        DispatcherIntegrationTestKit.ReadPayload(alphaRead).Should().Be(alphaMarker);

        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 4, fid: workFid, newFid: workFid, wname: new[] { "..", "beta" });

        var betaRead = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 5, fid: workFid, offset: 0, count: 128);
        DispatcherIntegrationTestKit.ReadPayload(betaRead).Should().Be(betaMarker);
    }

    [Fact]
    public async Task Dispatcher_Namespace_Read_NonZeroOffset_Returns_FollowOn_Page()
    {
        var backendNames = Enumerable.Range(0, 40).Select(i => $"b{i:000}").ToList();
        var backends = backendNames
            .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MarkerFileSystem(name)))
            .ToArray();

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(backends);

        const uint rootFid = 220;
        const uint pageBytes = 320;

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);

        var page1 = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 2, fid: rootFid, offset: 0, count: pageBytes);
        var entries1 = DispatcherIntegrationTestKit.ParseStatsTable(page1.Data.Span);
        entries1.Should().NotBeEmpty();

        ulong nextOffset = (ulong)page1.Data.Length;
        var page2 = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 3, fid: rootFid, offset: nextOffset, count: pageBytes);
        var entries2 = DispatcherIntegrationTestKit.ParseStatsTable(page2.Data.Span);

        entries2.Should().NotBeEmpty("non-zero read offsets should advance to the next namespace page");
        entries1.Select(e => e.Name).Intersect(entries2.Select(e => e.Name)).Should().BeEmpty();
        entries2.Select(e => e.Name).Should().OnlyContain(name => backendNames.Contains(name));
    }

    [Property(MaxTest = 40)]
    public bool Dispatcher_Namespace_BackendSwitch_Property(string rawAlpha, string rawBeta)
    {
        string alphaName = DispatcherIntegrationTestKit.CleanMount(rawAlpha, 1);
        string betaName = DispatcherIntegrationTestKit.CleanMount(rawBeta, 2);
        if (alphaName == betaName)
        {
            betaName += "_b";
        }

        string alphaMarker = "marker:" + alphaName;
        string betaMarker = "marker:" + betaName;

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/" + alphaName, () => new MarkerFileSystem(alphaMarker)),
            new StubBackend("/" + betaName, () => new MarkerFileSystem(betaMarker))
        });

        const uint rootFid = 300;
        const uint workFid = 301;

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: rootFid, newFid: workFid, wname: new[] { alphaName }).Sync();

        var before = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 3, fid: workFid, offset: 0, count: 128).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 4, fid: workFid, newFid: workFid, wname: new[] { "..", betaName }).Sync();
        var after = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 5, fid: workFid, offset: 0, count: 128).Sync();

        return DispatcherIntegrationTestKit.ReadPayload(before) == alphaMarker
            && DispatcherIntegrationTestKit.ReadPayload(after) == betaMarker;
    }

    [Property(MaxTest = 32)]
    public bool Dispatcher_Namespace_Read_Pagination_Property(PositiveInt backendCountSeed, PositiveInt entriesPerPageSeed)
    {
        int backendCount = Math.Clamp(backendCountSeed.Get % 48 + 12, 12, 60);
        int entriesPerPage = Math.Clamp(entriesPerPageSeed.Get % 8 + 1, 1, 8);

        var backendNames = Enumerable.Range(0, backendCount).Select(i => $"p{i:000}").ToList();
        var backends = backendNames
            .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MarkerFileSystem(name)))
            .ToArray();

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(backends);
        const uint rootFid = 410;
        uint pageBytes = (uint)(entriesPerPage * 160);

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid).Sync();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        ulong offset = 0;

        for (int step = 0; step < backendCount + 8; step++)
        {
            var page = DispatcherIntegrationTestKit.ReadAsync(
                    dispatcher,
                    tag: (ushort)(step + 2),
                    fid: rootFid,
                    offset: offset,
                    count: pageBytes)
                .GetAwaiter()
                .GetResult();

            var entries = DispatcherIntegrationTestKit.ParseStatsTable(page.Data.Span);
            if (entries.Count == 0)
            {
                break;
            }

            foreach (var entry in entries)
            {
                if (!seen.Add(entry.Name))
                {
                    return false;
                }
            }

            offset += (ulong)page.Data.Length;
        }

        return seen.SetEquals(backendNames);
    }

    [Fact]
    public async Task Dispatcher_Namespace_BackendSwitch_Fuzz_NoStickyDelegation()
    {
        var random = new Random(20260226);

        for (int iteration = 0; iteration < 70; iteration++)
        {
            string alphaName = $"a{iteration:00}_{random.Next(1000, 9999)}";
            string betaName = $"b{iteration:00}_{random.Next(1000, 9999)}";
            string alphaMarker = "marker:" + alphaName;
            string betaMarker = "marker:" + betaName;

            var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
            {
                new StubBackend("/" + alphaName, () => new MarkerFileSystem(alphaMarker)),
                new StubBackend("/" + betaName, () => new MarkerFileSystem(betaMarker))
            });

            const uint rootFid = 500;
            const uint workFid = 501;

            await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);
            await DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: rootFid, newFid: workFid, wname: new[] { alphaName });

            for (int step = 0; step < 14; step++)
            {
                bool switchToAlpha = random.Next(2) == 0;
                string targetName = switchToAlpha ? alphaName : betaName;
                string targetMarker = switchToAlpha ? alphaMarker : betaMarker;

                if (random.Next(2) == 0)
                {
                    await DispatcherIntegrationTestKit.WalkAsync(
                        dispatcher,
                        tag: (ushort)(10 + step * 2),
                        fid: workFid,
                        newFid: workFid,
                        wname: new[] { "..", targetName });
                }
                else
                {
                    await DispatcherIntegrationTestKit.WalkAsync(
                        dispatcher,
                        tag: (ushort)(10 + step * 2),
                        fid: workFid,
                        newFid: workFid,
                        wname: new[] { ".." });

                    await DispatcherIntegrationTestKit.WalkAsync(
                        dispatcher,
                        tag: (ushort)(11 + step * 2),
                        fid: workFid,
                        newFid: workFid,
                        wname: new[] { targetName });
                }

                var read = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: (ushort)(200 + step), fid: workFid, offset: 0, count: 128);
                DispatcherIntegrationTestKit.ReadPayload(read).Should().Be(targetMarker, $"fuzz iteration {iteration} step {step}");
            }
        }
    }

    [Fact]
    public async Task Dispatcher_Namespace_Read_Fuzz_Enumerates_All_Backends()
    {
        var random = new Random(9001);

        for (int iteration = 0; iteration < 28; iteration++)
        {
            int backendCount = random.Next(16, 58);
            int entriesPerPage = random.Next(1, 8);
            uint pageBytes = (uint)(entriesPerPage * 160);

            var backendNames = Enumerable.Range(0, backendCount).Select(i => $"f{i:000}").ToList();
            var backends = backendNames
                .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MarkerFileSystem(name)))
                .ToArray();

            var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(backends);
            const uint rootFid = 601;
            await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            ulong offset = 0;

            for (int step = 0; step < backendCount + 8; step++)
            {
                var page = await DispatcherIntegrationTestKit.ReadAsync(
                    dispatcher,
                    tag: (ushort)(step + 2),
                    fid: rootFid,
                    offset: offset,
                    count: pageBytes);

                var entries = DispatcherIntegrationTestKit.ParseStatsTable(page.Data.Span);
                if (entries.Count == 0)
                {
                    break;
                }

                foreach (var entry in entries)
                {
                    seen.Add(entry.Name);
                }

                offset += (ulong)page.Data.Length;
            }

            seen.Should().BeEquivalentTo(backendNames, $"fuzz iteration {iteration} should enumerate full namespace listing");
        }
    }
}

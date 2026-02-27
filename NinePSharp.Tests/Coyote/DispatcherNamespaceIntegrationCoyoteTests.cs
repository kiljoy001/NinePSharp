using NinePSharp.Constants;
using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherNamespaceIntegrationCoyoteTests
{
    [Fact]
    public void Coyote_Namespace_Switch_On_OneFid_DoesNot_Leak_To_OtherFid()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(300)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            const uint rootFid = 900;
            const uint fidA = 901;
            const uint fidB = 902;

            var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
            {
                new StubBackend("/alpha", () => new MarkerFileSystem("alpha")),
                new StubBackend("/beta", () => new MarkerFileSystem("beta"))
            });

            await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);
            await DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: rootFid, newFid: fidA, wname: new[] { "alpha" });
            await DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 3, fid: rootFid, newFid: fidB, wname: new[] { "alpha" });

            var switchAndRead = CoyoteTask.Run(async () =>
            {
                _ = await dispatcher.DispatchAsync(
                    NinePMessage.NewMsgTwalk(new Twalk(4, fidA, fidA, new[] { "..", "beta" })),
                    dialect: NinePDialect.NineP2000U);

                await CoyoteTask.Yield();

                var response = await dispatcher.DispatchAsync(
                    NinePMessage.NewMsgTread(new Tread(5, fidA, 0, 64)),
                    dialect: NinePDialect.NineP2000U);

                if (response is not Rread read)
                {
                    throw new Exception($"Expected Rread for switched fid, got {response.GetType().Name}");
                }

                return DispatcherIntegrationTestKit.ReadPayload(read);
            });

            var untouchedRead = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();

                var response = await dispatcher.DispatchAsync(
                    NinePMessage.NewMsgTread(new Tread(6, fidB, 0, 64)),
                    dialect: NinePDialect.NineP2000U);

                if (response is not Rread read)
                {
                    throw new Exception($"Expected Rread for untouched fid, got {response.GetType().Name}");
                }

                return DispatcherIntegrationTestKit.ReadPayload(read);
            });

            var outcomes = await CoyoteTask.WhenAll(switchAndRead, untouchedRead);
            string switchedPayload = outcomes[0];
            string untouchedPayload = outcomes[1];

            if (switchedPayload != "beta" || untouchedPayload != "alpha")
            {
                throw new Exception(
                    $"Namespace isolation violation. Switched fid payload='{switchedPayload}', untouched fid payload='{untouchedPayload}'.");
            }
        });

        engine.Run();
        engine.TestReport.NumOfFoundBugs.Should().Be(0, engine.TestReport.GetText(configuration));
    }

    [Fact]
    public void Coyote_Namespace_Concurrent_Read_Uses_Stable_Pagination_Offsets()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(200)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var names = Enumerable.Range(0, 20).Select(i => $"n{i:000}").ToArray();
            var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(
                names.Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MarkerFileSystem(name))).ToArray());

            const uint rootFid = 950;
            const uint pageBytes = 320;

            await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: rootFid);

            var first = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 2, fid: rootFid, offset: 0, count: pageBytes);
            var firstEntries = DispatcherIntegrationTestKit.ParseStatsTable(first.Data.Span);
            if (firstEntries.Count == 0)
            {
                throw new Exception("Expected first directory read page to be non-empty");
            }

            ulong secondOffset = (ulong)first.Data.Length;

            var pageA = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                var page = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 3, fid: rootFid, offset: secondOffset, count: pageBytes);
                return DispatcherIntegrationTestKit.ParseStatsTable(page.Data.Span).Select(e => e.Name).ToArray();
            });

            var pageB = CoyoteTask.Run(async () =>
            {
                var page = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 4, fid: rootFid, offset: secondOffset, count: pageBytes);
                await CoyoteTask.Yield();
                return DispatcherIntegrationTestKit.ParseStatsTable(page.Data.Span).Select(e => e.Name).ToArray();
            });

            var pages = await CoyoteTask.WhenAll(pageA, pageB);
            if (pages[0].Length == 0 || pages[1].Length == 0)
            {
                throw new Exception("Expected both concurrent directory reads to return a non-empty page");
            }

            if (!pages[0].SequenceEqual(pages[1]))
            {
                throw new Exception("Concurrent directory reads from the same offset should produce deterministic page contents");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}

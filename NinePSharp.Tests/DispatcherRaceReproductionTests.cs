using NinePSharp.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using Moq;

namespace NinePSharp.Tests;

public class DispatcherRaceReproductionTests
{
    private static INinePFileSystem CreateSlowFs(int delayMs = 20)
    {
        var mock = new Mock<INinePFileSystem>();
        
        mock.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
            .Returns(async (Twalk t) => {
                if (delayMs > 0) await Task.Delay(delayMs);
                return new Rwalk(t.Tag, new[] { new Qid(QidType.QTDIR, 0, (ulong)DateTime.Now.Ticks) });
            });

        mock.Setup(x => x.Clone()).Returns(() => CreateSlowFs(delayMs));
        mock.Setup(x => x.ClunkAsync(It.IsAny<Tclunk>())).ReturnsAsync((Tclunk t) => new Rclunk(t.Tag));
        
        return mock.Object;
    }

    [Fact]
    public async Task Dispatcher_Twalk_Direct_Dictionary_Race_Reproduction()
    {
        // This test simulates the logic inside DispatchAsync but stripped down
        // to expose the race between ContainsKey and assignment in a loop.
        
        var mockBackend = new Mock<IProtocolBackend>();
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IRemoteMountProvider>().Object);

        uint rootFid = 100;
        uint newFid = 101;

        // Force attach root
        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(0, rootFid, NinePConstants.NoFid, "user", "/")), NinePDialect.NineP2000U);

        int totalSuccesses = 0;
        int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            // We want to see if we can get successCount > 1 for a single "logical" Twalk operation
            // that targets an UNUSED newFid.
            
            var startSignal = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, 20).Select(tag => Task.Run(async () => {
                startSignal.Wait();
                // This call should perform: if(ContainsKey) return error; await walk; assignment;
                return await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk((ushort)tag, rootFid, newFid, new[] { "data" })), NinePDialect.NineP2000U);
            })).ToList();

            startSignal.Set();
            var results = await Task.WhenAll(tasks);
            
            int successes = results.Count(r => r is Rwalk);
            if (successes > 1)
            {
                totalSuccesses += successes;
                // Found it!
                Assert.Fail($"RACE DETECTED: {successes} threads assigned FID {newFid} simultaneously on iteration {i}!");
            }

            // Cleanup for next iteration
            await dispatcher.DispatchAsync(NinePMessage.NewMsgTclunk(new Tclunk(0, newFid)), NinePDialect.NineP2000U);
        }
    }
}

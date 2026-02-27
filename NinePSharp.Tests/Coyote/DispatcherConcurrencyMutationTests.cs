using NinePSharp.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
using Xunit;
using Moq;
using FluentAssertions;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherConcurrencyMutationTests
{
    private static Microsoft.Coyote.Configuration CreateCoyoteConfiguration(uint iterations)
    {
        return Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(iterations)
            .WithPartiallyControlledConcurrencyAllowed(true);
    }

    [Fact]
    public void Coyote_Dispatcher_Twalk_Concurrent_NewFid_Enforces_Single_Claim()
    {
        var configuration = CreateCoyoteConfiguration(iterations: 1000);
        var engine = TestingEngine.Create(configuration, async () =>
        {
            var mockBackend = new Mock<IProtocolBackend>();
            mockBackend.Setup(b => b.Name).Returns("mock");
            mockBackend.Setup(b => b.MountPath).Returns("/mock");
            
            // USE A MOCK THAT LETS COYOTE INTERLEAVE DURING WALK
            var mockFs = new Mock<INinePFileSystem>();
            mockFs.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
                  .Returns(async (Twalk t) => {
                      // CRITICAL: Force a context switch here
                      await CoyoteTask.Yield(); 
                      return new Rwalk(t.Tag, new[] { new Qid(QidType.QTDIR, 0, 0) });
                  });
            mockFs.Setup(x => x.Clone()).Returns(mockFs.Object); // Shared for this test to simplify

            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>()))
                       .Returns(mockFs.Object);
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<System.Security.SecureString>(), It.IsAny<X509Certificate2>()))
                       .Returns(mockFs.Object);

            var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IClusterManager>().Object);

            uint rootFid = 100;
            uint newFid = 101;

            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(0, rootFid, NinePConstants.NoFid, "user", "mock")), NinePDialect.NineP2000U);

            // Trigger race: 2 concurrent walks to SAME newfid
            var task1 = CoyoteTask.Run(async () => 
                await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(1, rootFid, newFid, new[] { "data" })), NinePDialect.NineP2000U));
            
            var task2 = CoyoteTask.Run(async () => 
                await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(2, rootFid, newFid, new[] { "data" })), NinePDialect.NineP2000U));

            var results = await CoyoteTask.WhenAll(task1, task2);

            int successCount = results.Count(r => r is Rwalk rw && rw.Wqid != null && rw.Wqid.Length == 1);
            int errorCount = results.Count(r => r is Rerror || r is Rlerror);

            // INVARIANT: Exactly one Twalk may claim a given newfid.
            if (successCount != 1 || errorCount != 1)
            {
                throw new Exception($"Invariant violated: success={successCount}, error={errorCount}");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}

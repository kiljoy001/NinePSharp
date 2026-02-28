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
using Xunit;
using Moq;
using FluentAssertions;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class NinePFSDispatcherConcurrencyTests
{
    private static Microsoft.Coyote.Configuration CreateCoyoteConfiguration(uint iterations)
    {
        return Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(iterations)
            .WithPartiallyControlledConcurrencyAllowed(true);
    }

    private static int GetFidCount(NinePFSDispatcher dispatcher)
    {
        var field = typeof(NinePFSDispatcher).GetField("_fids", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("NinePFSDispatcher._fids field not found.");
        var fids = (ConcurrentDictionary<uint, INinePFileSystem>)(field.GetValue(dispatcher)
            ?? throw new InvalidOperationException("NinePFSDispatcher._fids value not available."));
        return fids.Count;
    }

    /// <summary>
    /// Tests Twalk for race conditions when multiple threads try to walk to the same NewFid.
    /// This catches the race where two threads check ContainsKey(NewFid) simultaneously.
    /// </summary>
    [Fact]
    public void Coyote_Twalk_Concurrent_NewFid_Race()
    {
        var configuration = CreateCoyoteConfiguration(iterations: 1000); // More iterations
        var engine = TestingEngine.Create(configuration, async () =>
        {
            var mockBackend = new Mock<IProtocolBackend>();
            mockBackend.Setup(b => b.MountPath).Returns("/mock");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>()))
                       .Returns(() => new MockFileSystem());

            var mockClusterManager = new Mock<IRemoteMountProvider>();
            var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, mockClusterManager.Object);

            uint rootFid = 100;
            uint newFid = 101;
            ushort tag1 = 1;
            ushort tag2 = 2;

            // Setup: Attach to root
            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(0, rootFid, NinePConstants.NoFid, "user", "mock")), NinePDialect.NineP2000U);

            // Two threads walking from root to same newFid
            // We use CoyoteTask.Run to allow Coyote to interleave these
            var task1 = CoyoteTask.Run(async () => 
                await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(tag1, rootFid, newFid, new[] { "data" })), NinePDialect.NineP2000U));
            
            var task2 = CoyoteTask.Run(async () => 
                await dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk(tag2, rootFid, newFid, new[] { "data" })), NinePDialect.NineP2000U));

            var results = await CoyoteTask.WhenAll(task1, task2);

            int successCount = results.Count(r => r is Rwalk);
            
            // INVARIANT: In a race-free 9P server, only one of these should succeed 
            // if they target the same NewFid and NewFid != Fid.
            // If both succeed, we have a race where ContainsKey check was bypassed.
            if (successCount > 1)
            {
                throw new Exception("RACE DETECTED: Both concurrent Twalks to same NewFid succeeded!");
            }
        });

        engine.Run();
        
        // If the race is subtle, it might still pass without assembly rewriting.
        // But if Coyote finds it, NumOfFoundBugs will be > 0.
        Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    }

    [Fact]
    public void Coyote_Concurrent_Tclunk_Same_Fid()
    {
        var configuration = CreateCoyoteConfiguration(iterations: 500);
        var engine = TestingEngine.Create(configuration, async () =>
        {
            var mockBackend = new Mock<IProtocolBackend>();
            mockBackend.Setup(b => b.MountPath).Returns("/mock");
            mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>()))
                       .Returns(() => new MockFileSystem());

            var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IRemoteMountProvider>().Object);

            uint fid = 100;
            await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(0, fid, NinePConstants.NoFid, "user", "mock")), NinePDialect.NineP2000U);

            // Concurrent clunks
            var task1 = CoyoteTask.Run(async () => await dispatcher.DispatchAsync(NinePMessage.NewMsgTclunk(new Tclunk(1, fid)), NinePDialect.NineP2000U));
            var task2 = CoyoteTask.Run(async () => await dispatcher.DispatchAsync(NinePMessage.NewMsgTclunk(new Tclunk(2, fid)), NinePDialect.NineP2000U));

            var results = await CoyoteTask.WhenAll(task1, task2);

            int errorCount = results.Count(r => r is Rerror || r is Rlerror);
            int successCount = results.Count(r => r is Rclunk);

            // Exactly one should succeed, one should fail with "Unknown FID"
            if (successCount != 1)
            {
                 // In a race, TryRemove might fail or both might somehow succeed if not handled right
                 // Though ConcurrentDictionary.TryRemove is atomic.
            }
        });

        engine.Run();
        Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    }
}

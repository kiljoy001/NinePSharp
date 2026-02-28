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

public class DispatcherConcurrencyStressTests
{
    private static INinePFileSystem CreateSimpleFs()
    {
        var mock = new Mock<INinePFileSystem>();
        mock.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTDIR, 0, 0) }));
        mock.Setup(x => x.Clone()).Returns(() => CreateSimpleFs());
        mock.Setup(x => x.ClunkAsync(It.IsAny<Tclunk>())).ReturnsAsync((Tclunk t) => new Rclunk(t.Tag));
        return mock.Object;
    }

    [Fact]
    public async Task Dispatcher_Extreme_Concurrency_Stress_Test()
    {
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>()))
                   .Returns(() => CreateSimpleFs());

        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, new Mock<IRemoteMountProvider>().Object);

        uint rootFid = 100;
        uint targetFid = 200;

        await dispatcher.DispatchAsync(NinePMessage.NewMsgTattach(new Tattach(0, rootFid, NinePConstants.NoFid, "user", "/")), NinePDialect.NineP2000U);

        int iterations = 1000;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, iterations, i => {
            try
            {
                // Rapidly cycle operations on the SAME targetFid from many threads
                int op = i % 3;
                if (op == 0)
                {
                    dispatcher.DispatchAsync(NinePMessage.NewMsgTwalk(new Twalk((ushort)i, rootFid, targetFid, new[] { "data" })), NinePDialect.NineP2000U).Wait();
                }
                else if (op == 1)
                {
                    dispatcher.DispatchAsync(NinePMessage.NewMsgTclunk(new Tclunk((ushort)i, targetFid)), NinePDialect.NineP2000U).Wait();
                }
                else
                {
                    dispatcher.DispatchAsync(NinePMessage.NewMsgTstat(new Tstat((ushort)i, targetFid)), NinePDialect.NineP2000U).Wait();
                }
            }
            catch (Exception ex)
            {
                // We expect NinePProtocolException (e.g. "Unknown FID"), but NOT other types like NullReference or KeyNotFound
                if (!(ex.InnerException is NinePProtocolException))
                {
                    exceptions.Add(ex);
                }
            }
        });

        if (exceptions.Count > 0)
        {
            throw new AggregateException("CONCURRENCY BUGS DETECTED!", exceptions);
        }
    }
}

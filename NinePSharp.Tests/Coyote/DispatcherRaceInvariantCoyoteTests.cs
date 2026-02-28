using NinePSharp.Constants;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherRaceInvariantCoyoteTests
{
    [Fact]
    public void Coyote_Twalk_Concurrent_SameNewFid_Yields_OneSuccess_OneError()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(250)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var fs = new Mock<INinePFileSystem>(MockBehavior.Strict);
            fs.Setup(f => f.Clone()).Returns(fs.Object);
            fs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
                .Returns(async (Twalk t) =>
                {
                    await CoyoteTask.Yield();
                    return new Rwalk(t.Tag, t.Wname.Select(_ => new Qid(QidType.QTDIR, 0, 1)).ToArray());
                });

            var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
            backend.SetupGet(b => b.Name).Returns("mock");
            backend.SetupGet(b => b.MountPath).Returns("/mock");
            backend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(fs.Object);
            backend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(fs.Object);

            var dispatcher = new NinePFSDispatcher(
                NullLogger<NinePFSDispatcher>.Instance,
                new[] { backend.Object },
                new Mock<IRemoteMountProvider>().Object);

            await dispatcher.DispatchAsync(
                NinePMessage.NewMsgTattach(new Tattach(1, 100, NinePConstants.NoFid, "user", "/mock")),
                dialect: NinePDialect.NineP2000U);

            const uint newFid = 200;
            var twalk1 = CoyoteTask.Run(() => dispatcher.DispatchAsync(
                NinePMessage.NewMsgTwalk(new Twalk(2, 100, newFid, new[] { "x" })),
                dialect: NinePDialect.NineP2000U));
            var twalk2 = CoyoteTask.Run(() => dispatcher.DispatchAsync(
                NinePMessage.NewMsgTwalk(new Twalk(3, 100, newFid, new[] { "x" })),
                dialect: NinePDialect.NineP2000U));

            object[] results = await CoyoteTask.WhenAll(twalk1, twalk2);

            int walkSuccesses = results.Count(r => r is Rwalk rw && rw.Wqid != null && rw.Wqid.Length == 1);
            int errors = results.Count(r => r is Rerror || r is Rlerror);

            walkSuccesses.Should().Be(1, "newfid can be claimed by only one concurrent Twalk");
            errors.Should().Be(1, "the loser should receive an error response");
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}

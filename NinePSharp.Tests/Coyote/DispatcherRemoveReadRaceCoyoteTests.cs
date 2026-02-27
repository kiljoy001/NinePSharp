using NinePSharp.Constants;
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
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Interfaces;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class DispatcherRemoveReadRaceCoyoteTests
{
    [Fact]
    public void Coyote_Tremove_Concurrent_With_Tread_Leaves_Fid_Clunked()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(200)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var fs = new Mock<INinePFileSystem>(MockBehavior.Strict);
            fs.Setup(f => f.Clone()).Returns(fs.Object);
            fs.Setup(f => f.ReadAsync(It.IsAny<Tread>()))
                .Returns(async (Tread t) =>
                {
                    await CoyoteTask.Yield();
                    return new Rread(t.Tag, new byte[] { 0x42 });
                });
            fs.Setup(f => f.RemoveAsync(It.IsAny<Tremove>()))
                .Returns(async (Tremove t) =>
                {
                    await CoyoteTask.Yield();
                    return new Rremove(t.Tag);
                });

            var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
            backend.SetupGet(b => b.Name).Returns("mock");
            backend.SetupGet(b => b.MountPath).Returns("/mock");
            backend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(fs.Object);
            backend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(fs.Object);

            var dispatcher = new NinePFSDispatcher(
                NullLogger<NinePFSDispatcher>.Instance,
                new[] { backend.Object },
                new Mock<IClusterManager>().Object);

            _ = await dispatcher.DispatchAsync(
                NinePMessage.NewMsgTattach(new Tattach(1, 100, NinePConstants.NoFid, "user", "/mock")),
                dialect: NinePDialect.NineP2000U);

            var readTask = CoyoteTask.Run(() => dispatcher.DispatchAsync(
                NinePMessage.NewMsgTread(new Tread(2, 100, 0, 1)),
                dialect: NinePDialect.NineP2000U));
            var removeTask = CoyoteTask.Run(() => dispatcher.DispatchAsync(
                NinePMessage.NewMsgTremove(new Tremove(3, 100)),
                dialect: NinePDialect.NineP2000U));

            object[] results = await CoyoteTask.WhenAll(readTask, removeTask);
            results.Count(r => r is Rremove).Should().Be(1);
            results.Count(r => r is Rread || r is Rerror || r is Rlerror).Should().Be(1);

            var followUpRead = await dispatcher.DispatchAsync(
                NinePMessage.NewMsgTread(new Tread(4, 100, 0, 1)),
                dialect: NinePDialect.NineP2000U);
            followUpRead.Should().Match(r => r is Rerror || r is Rlerror);

            fs.Verify(f => f.RemoveAsync(It.IsAny<Tremove>()), Times.Once);
            fs.Verify(f => f.ReadAsync(It.IsAny<Tread>()), Times.AtMostOnce);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }
}

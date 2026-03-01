using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Abstractions.Utils;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public sealed class DispatcherSessionAndBackendRuntimeTests
{
    [Fact]
    public async Task DispatchAsync_Requires_Explicit_SessionId()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            Array.Empty<IProtocolBackend>(),
            new NullRemoteMountProvider());

        Func<Task> act = async () => await dispatcher.DispatchAsync(string.Empty, NinePMessage.NewMsgTflush(new Tflush(1, 0)), NinePDialect.NineP2000);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FileSystemBackendRuntime_Walks_Path_Before_Dispatching_Read()
    {
        var fs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        fs.SetupProperty(x => x.Dialect, NinePDialect.NineP2000);
        fs.Setup(x => x.WalkAsync(It.Is<Twalk>(w => w.Wname.Length == 2 && w.Wname[0] == "alpha" && w.Wname[1] == "beta")))
            .ReturnsAsync(new Rwalk(0, new[]
            {
                new Qid(QidType.QTDIR, 0, 1),
                new Qid(QidType.QTFILE, 0, 2)
            }));
        fs.Setup(x => x.ReadAsync(It.IsAny<Tread>()))
            .ReturnsAsync(new Rread(7, new byte[] { 1, 2, 3 }));

        var runtime = new FileSystemBackendRuntime("local", "/local", () => fs.Object);

        var response = await runtime.ReadAsync(new[] { "alpha", "beta" }, new Tread(7, 0, 0, 3), NinePDialect.NineP2000U);

        response.Data.ToArray().Should().Equal(1, 2, 3);
        fs.Object.Dialect.Should().Be(NinePDialect.NineP2000U);
        fs.VerifyAll();
    }

    [Fact]
    public async Task FileSystemBackendRuntime_Rejects_Missing_Path()
    {
        var fs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        fs.SetupProperty(x => x.Dialect, NinePDialect.NineP2000);
        fs.Setup(x => x.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync(new Rwalk(0, new[] { new Qid(QidType.QTDIR, 0, 1) }));

        var runtime = new FileSystemBackendRuntime("local", "/local", () => fs.Object);

        Func<Task> act = async () => await runtime.StatAsync(new[] { "only", "partial" }, new Tstat(9, 0), NinePDialect.NineP2000);

        await act.Should().ThrowAsync<NinePSharp.Server.Utils.NinePProtocolException>()
            .WithMessage("*no longer exists*");
    }
}

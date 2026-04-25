using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Tests.Helpers;

namespace NinePSharp.Tests;

public class NinePFSDispatcherTests
{
    [Fact]
    public async Task Constructor_WithDirectHandler_DispatchesMessages()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new MarkerFileSystem("alpha"));

        var response = await dispatcher.DispatchAsync(
            "session-1",
            NinePMessage.NewMsgTversion(new Tversion(1, 8192, "9P2000")),
            NinePDialect.NineP2000);

        var version = Assert.IsType<Rversion>(response);
        Assert.Equal((ushort)1, version.Tag);
        Assert.Equal("9P2000", version.Version);
    }
}

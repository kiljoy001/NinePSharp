using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using NinePSharp.Parser;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class AInFlightCompletionTests
{
    [Fact]
    public async Task ClassicErrorsRetainTheRequestTag()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();

        object response = await test.Dispatcher.DispatchAsync(
            "unit",
            NinePMessage.NewMsgTattach(new Tattach(40, 1, NinePConstants.NoFid, "user", "/")),
            NinePDialect.NineP2000);

        Assert.Equal((ushort)40, Assert.IsType<Rerror>(response).Tag);
    }

    [Fact]
    public async Task WriteReturnsAResponseWithoutRequiringTheWireClient()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();

        object response = await test.Dispatcher.DispatchAsync(
            "unit",
            NinePMessage.NewMsgTwrite(new Twrite(52, 2, 0, new byte[] { 1 })),
            NinePDialect.NineP2000);

        var written = Assert.IsType<Rwrite>(response);
        Assert.Equal((ushort)52, written.Tag);
    }

    [Fact]
    public async Task FlushCompletesPromptlyAfterTheTargetRequestFinishes()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Resources.Setup(value => value.ReadAsync(
                It.IsAny<ResourceOpenHandle>(),
                It.IsAny<ulong>(),
                It.IsAny<uint>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ResourceOpenHandle handle, ulong offset, uint count, CancellationToken token) =>
            {
                started.TrySetResult();
                await release.Task;
                return ReadOnlyMemory<byte>.Empty;
            });

        Task<object> read = test.Dispatcher.DispatchAsync(
            "unit",
            NinePMessage.NewMsgTread(new Tread(50, 2, 0, 1)),
            NinePDialect.NineP2000);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task<object> flush = test.Dispatcher.DispatchAsync(
            "unit",
            NinePMessage.NewMsgTflush(new Tflush(51, 50)),
            NinePDialect.NineP2000);

        release.TrySetResult();
        Assert.IsType<Rread>(await read.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.IsType<Rflush>(await flush.WaitAsync(TimeSpan.FromMilliseconds(100)));
    }
}

using Microsoft.Extensions.Logging;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;

namespace NinePSharp.Tests;

// A connection that has ended waits at most its drain limit for its session to close and its
// requests to finish; what is left has an unknown outcome.
public sealed class ConnectionDrainTests
{
    private static readonly TimeSpan Drain = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task ARequestThatNeverAnswersIsLeftWithAnUnknownOutcome()
    {
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .Returns(new TaskCompletionSource<object>().Task);
        var logger = new Warnings();

        await Process(dispatcher.Object, logger, new Tclunk(7, 1));

        Assert.Equal(["Requests from (null) did not finish within the drain limit (1 left); their outcome is unknown."], logger.Messages);
    }

    [Fact]
    public async Task ASessionCloseThatNeverFinishesIsLeftWithAnUnknownOutcome()
    {
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.As<INinePSessionLifecycle>().Setup(value => value.CloseSessionAsync(It.IsAny<string>())).Returns(new TaskCompletionSource().Task);
        var logger = new Warnings();

        await Process(dispatcher.Object, logger);

        Assert.Equal(["Closing the session of (null) did not finish within the drain limit; its outcome is unknown."], logger.Messages);
    }

    [Fact]
    public async Task WorkThatFinishesLogsNoUnknownOutcome()
    {
        var dispatcher = new Mock<INinePFSDispatcher>();
        dispatcher.Setup(value => value.DispatchAsync(It.IsAny<string>(), It.IsAny<NinePMessage>(), It.IsAny<NinePDialect>(), null))
            .ReturnsAsync(new Rclunk(7));
        dispatcher.As<INinePSessionLifecycle>().Setup(value => value.CloseSessionAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var logger = new Warnings();

        await Process(dispatcher.Object, logger, new Tclunk(7, 1));

        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void TheDefaultDrainIsFiveSeconds()
        => Assert.Equal(TimeSpan.FromSeconds(5), new NinePConnectionProcessor(new Warnings(), new Mock<INinePFSDispatcher>().Object).Drain);

    private static async Task Process(INinePFSDispatcher dispatcher, ILogger logger, params ISerializable[] requests)
    {
        var processor = new NinePConnectionProcessor(logger, dispatcher) { Drain = Drain };
        byte[] input = requests.SelectMany(request =>
        {
            var bytes = new byte[request.Size];
            request.WriteTo(bytes);
            return bytes;
        }).ToArray();
        using var stream = new MemoryStream(input);
        await processor.ProcessStreamAsync(stream, null, new NinePConnectionProcessor.ClientSession(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class Warnings : ILogger
    {
        private readonly List<string> messages = new();

        internal IReadOnlyList<string> Messages
        {
            get
            {
                lock (messages)
                {
                    return messages.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}

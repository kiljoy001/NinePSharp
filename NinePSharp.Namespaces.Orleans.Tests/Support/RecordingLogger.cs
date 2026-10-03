using Microsoft.Extensions.Logging;

namespace NinePSharp.Namespaces.Orleans.Tests.Support;

internal sealed class RecordingLogger : ILogger
{
    private readonly List<string> warnings = new();

    internal IReadOnlyList<string> Warnings
    {
        get
        {
            lock (warnings)
            {
                return warnings.ToArray();
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
            lock (warnings)
            {
                warnings.Add(formatter(state, exception));
            }
        }
    }
}

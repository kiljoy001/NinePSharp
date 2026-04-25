using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server.FSharp;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

public sealed class NinePFSDispatcher : INinePFSDispatcher
{
    private readonly INinePFSDispatcher _engine;

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, INinePRequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(handler);
        _engine = new NinePFSDispatcherEngine(handler);
    }

    public Task<object> DispatchAsync(string sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => _engine.DispatchAsync(sessionId, message, dialect, certificate);
}

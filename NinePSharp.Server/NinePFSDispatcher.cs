using System.Collections.Generic;
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

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends, IRemoteMountProvider remoteMountProvider)
    {
        _ = logger;
        _engine = new NinePFSDispatcherEngine(new DefaultAttachResolver(backends, remoteMountProvider));
    }

    public Task<object> DispatchAsync(NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => _engine.DispatchAsync(message, dialect, certificate);

    public Task<object> DispatchAsync(string? sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => _engine.DispatchAsync(sessionId, message, dialect, certificate);
}

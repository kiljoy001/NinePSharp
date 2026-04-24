using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server.FSharp;
using NinePSharp.Server.Interfaces;
using System.Linq;

namespace NinePSharp.Server;

public sealed class NinePFSDispatcher : INinePFSDispatcher
{
    private readonly INinePFSDispatcher _engine;

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends, IRemoteMountProvider remoteMountProvider)
    {
        _ = logger;
        // For now, we take the first backend's handler to bridge to the F# engine.
        // A full implementation would handle union mounts in the dispatcher.
        var firstBackend = backends.FirstOrDefault();
        if (firstBackend == null)
        {
             throw new System.ArgumentException("At least one backend is required.");
        }
        
        var handler = firstBackend.GetFileSystem();
        _engine = new NinePFSDispatcherEngine((INinePRequestHandler)handler);
    }

    public Task<object> DispatchAsync(string sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => _engine.DispatchAsync(sessionId, message, dialect, certificate);
}

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Constants;
using NinePSharp.Parser;
// using NinePSharp.Server.FSharp;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server;

public sealed class NinePFSDispatcher : INinePFSDispatcher
{
    // private readonly INinePFSDispatcher _engine;

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends, IRemoteMountProvider remoteMountProvider)
    {
        _ = logger;
        _ = backends;
        _ = remoteMountProvider;
        // _engine = new NinePFSDispatcherEngine(new DefaultAttachResolver(backends, remoteMountProvider));
    }

    public Task<object> DispatchAsync(string sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => throw new System.NotImplementedException("Dispatcher is temporarily disabled due to F# build issues. Use the new high-level FileSystem API instead.");
}

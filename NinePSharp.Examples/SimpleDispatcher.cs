using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Examples;
using NinePSharp.Server;
using NinePSharp.Server.FSharp;

namespace NinePSharp.Examples;

internal class SimpleDispatcher : INinePFSDispatcher
{
    private readonly INinePFSDispatcher engine;

    public SimpleDispatcher(INinePFSDispatcher engine) => this.engine = engine;

    public Task<object> DispatchAsync(string sessionId, NinePSharp.Parser.NinePMessage message, NinePSharp.Constants.NinePDialect dialect, X509Certificate2? certificate = null)
        => engine.DispatchAsync(sessionId, message, dialect, certificate);
}

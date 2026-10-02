using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Parser;

namespace NinePSharp.Server;

public interface INinePFSDispatcher
{
    Task<object> DispatchAsync(string sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null);
}

/// <summary>Receives notification when a transport connection and all of its fids are gone.</summary>
public interface INinePSessionLifecycle
{
    /// <summary>Cancels outstanding requests and releases connection-local state.</summary>
    Task CloseSessionAsync(string sessionId);
}

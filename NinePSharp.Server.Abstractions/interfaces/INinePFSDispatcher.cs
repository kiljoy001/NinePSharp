using NinePSharp.Parser;
using NinePSharp.Constants;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server;

public interface INinePFSDispatcher
{
    Task<object> DispatchAsync(NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null);

    Task<object> DispatchAsync(string? sessionId, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null);
}

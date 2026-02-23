using NinePSharp.Parser;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server;

public interface INinePFSDispatcher
{
    Task<object> DispatchAsync(NinePMessage message, bool dotu, X509Certificate2? certificate = null);
}

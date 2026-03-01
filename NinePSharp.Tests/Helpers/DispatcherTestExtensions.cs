using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server;

namespace NinePSharp.Tests.Helpers;

internal static class DispatcherTestExtensions
{
    internal const string DefaultSessionId = "test-session";

    public static Task<object> DispatchAsync(this INinePFSDispatcher dispatcher, NinePMessage message, NinePDialect dialect, X509Certificate2? certificate = null)
        => dispatcher.DispatchAsync(DefaultSessionId, message, dialect, certificate);
}

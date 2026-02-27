using NinePSharp.Parser;
using NinePSharp.Constants;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server;

/// <summary>
/// Defines the dispatcher responsible for routing 9P messages to the appropriate filesystem backends.
/// </summary>
public interface INinePFSDispatcher
{
    /// <summary>
    /// Dispatches a parsed 9P message to the corresponding backend handler.
    /// </summary>
    /// <param name="message">The parsed 9P message from the F# parser.</param>
    /// <param name="dialect">The negotiated 9P dialect.</param>
    /// <param name="certificate">Optional client certificate for authentication.</param>
    /// <returns>A 9P response message object (e.g. Rversion, Rread, etc.).</returns>
    Task<object> DispatchAsync(NinePMessage message, NinePSharp.Constants.NinePDialect dialect, X509Certificate2? certificate = null);
}

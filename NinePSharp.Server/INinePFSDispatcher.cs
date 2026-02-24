using NinePSharp.Parser;
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
    /// <param name="dotu">True if using 9P2000.u/L extensions.</param>
    /// <param name="certificate">Optional client certificate for authentication.</param>
    /// <returns>A 9P response message object (e.g. Rversion, Rread, etc.).</returns>
    Task<object> DispatchAsync(NinePMessage message, bool dotu, X509Certificate2? certificate = null);
}

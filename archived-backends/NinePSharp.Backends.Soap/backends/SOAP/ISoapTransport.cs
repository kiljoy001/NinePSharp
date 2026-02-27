using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.SOAP;

/// <summary>
/// Interface for SOAP service communication.
/// </summary>
public interface ISoapTransport
{
    /// <summary>Connects to the SOAP service.</summary>
    Task ConnectAsync(string wsdlUrl);
    /// <summary>Calls a SOAP action.</summary>
    Task<string> CallActionAsync(string action, string xmlPayload, IDictionary<string, string> headers);
}

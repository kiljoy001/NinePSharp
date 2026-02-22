using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinePSharp.Server.Backends.SOAP;

public interface ISoapTransport
{
    Task ConnectAsync(string wsdlUrl);
    Task<string> CallActionAsync(string action, string xmlPayload, IDictionary<string, string> headers);
}

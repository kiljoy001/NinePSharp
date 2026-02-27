namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for the SOAP backend.
/// </summary>
public class SoapBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the URL of the WSDL definition.</summary>
    public string WsdlUrl { get; set; } = string.Empty;
}

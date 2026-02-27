namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for the REST backend.
/// </summary>
public class RestBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the base URL of the REST service.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

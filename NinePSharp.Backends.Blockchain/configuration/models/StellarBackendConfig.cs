using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

public class StellarBackendConfig : BackendConfigBase
{
    public string HorizonUrl { get; set; } = "https://horizon.stellar.org";
    public bool UsePublicNetwork { get; set; } = true;

    /// <summary>
    /// List of operations/methods allowed.
    /// </summary>
    public List<string> AllowedMethods { get; set; } = new();
}

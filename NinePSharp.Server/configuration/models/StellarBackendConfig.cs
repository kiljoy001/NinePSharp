namespace NinePSharp.Server.Configuration.Models;

public class StellarBackendConfig : BackendConfigBase
{
    public string HorizonUrl { get; set; } = "https://horizon.stellar.org";
    public bool UsePublicNetwork { get; set; } = true;
}

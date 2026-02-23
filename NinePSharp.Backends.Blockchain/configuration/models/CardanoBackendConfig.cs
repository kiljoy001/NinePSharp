using System.Collections.Generic;

namespace NinePSharp.Server.Configuration.Models;

public class CardanoBackendConfig : BackendConfigBase
{
    public string Network { get; set; } = "Mainnet";
    public string? BlockfrostProjectId { get; set; }
    public string? BlockfrostApiUrl { get; set; }

    /// <summary>
    /// List of operations/methods allowed.
    /// </summary>
    public List<string> AllowedMethods { get; set; } = new();
}

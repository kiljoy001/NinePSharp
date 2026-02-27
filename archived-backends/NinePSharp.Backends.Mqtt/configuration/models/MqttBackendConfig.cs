namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for the MQTT backend.
/// </summary>
public class MqttBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the URL of the MQTT broker.</summary>
    public string BrokerUrl { get; set; } = string.Empty;
    /// <summary>Gets or sets the unique client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;
}

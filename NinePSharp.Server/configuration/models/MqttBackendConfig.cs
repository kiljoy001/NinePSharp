namespace NinePSharp.Server.Configuration.Models;

public class MqttBackendConfig : BackendConfigBase
{
    public string BrokerUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}

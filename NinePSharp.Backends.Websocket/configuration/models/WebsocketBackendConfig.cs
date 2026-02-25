namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for the WebSocket backend.
/// </summary>
public class WebsocketBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the URL of the WebSocket server.</summary>
    public string Url { get; set; } = string.Empty;
}

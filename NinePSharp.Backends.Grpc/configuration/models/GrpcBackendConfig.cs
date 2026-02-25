namespace NinePSharp.Server.Configuration.Models;

/// <summary>
/// Configuration for the gRPC backend.
/// </summary>
public class GrpcBackendConfig : BackendConfigBase
{
    /// <summary>Gets or sets the host name or IP address.</summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>Gets or sets the TCP port.</summary>
    public int Port { get; set; }
}

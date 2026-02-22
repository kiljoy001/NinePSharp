namespace NinePSharp.Server.Configuration.Models;

public class GrpcBackendConfig : BackendConfigBase
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

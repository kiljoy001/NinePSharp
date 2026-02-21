namespace NinePSharp.Server.Configuration.Models;

public class EndpointConfig
{
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
}

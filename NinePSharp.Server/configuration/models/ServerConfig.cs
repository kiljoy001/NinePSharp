using System.Collections.Generic;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Configuration.Models;

public class ServerConfig
{
    public List<EndpointConfig> Endpoints { get; set; } = new();
}

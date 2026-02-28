using System.Collections.Generic;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Configuration.Models;

public class ServerConfig
{
    public List<EndpointConfig> Endpoints { get; set; } = new();
    public EmercoinConfig? Emercoin { get; set; }
}

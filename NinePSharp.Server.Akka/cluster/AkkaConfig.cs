using System.Collections.Generic;

namespace NinePSharp.Server.Cluster;

public sealed class AkkaConfig
{
    public string SystemName { get; set; } = "NinePCluster";
    public string Hostname { get; set; } = "localhost";
    public int Port { get; set; } = 8081;
    public string Role { get; set; } = "backend";
    public List<string> SeedNodes { get; set; } = new();
    public string? BindHostname { get; set; }
    public int? BindPort { get; set; }
    public string? PublicHostname { get; set; }
    public int? PublicPort { get; set; }
    public string? InterfaceName { get; set; }
    public bool PreferIPv6 { get; set; }
}

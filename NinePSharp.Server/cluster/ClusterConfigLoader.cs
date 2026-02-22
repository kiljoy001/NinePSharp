using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Cluster;

public static class ClusterConfigLoader
{
    public static AkkaConfig LoadFromTextFile(string filePath, ILogger logger)
    {
        var config = new AkkaConfig();
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Cluster config file '{FilePath}' not found. Using defaults.", filePath);
            return config;
        }

        logger.LogInformation("Loading cluster configuration from '{FilePath}'...", filePath);
        
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim();

            switch (key)
            {
                case "systemname": config.SystemName = value; break;
                case "hostname": config.Hostname = value; break;
                case "port": if (int.TryParse(value, out var p)) config.Port = p; break;
                case "bindhostname": config.BindHostname = value; break;
                case "bindport":
                    if (int.TryParse(value, out var bp)) config.BindPort = bp;
                    break;
                case "publichostname": config.PublicHostname = value; break;
                case "publicport":
                    if (int.TryParse(value, out var pp)) config.PublicPort = pp;
                    break;
                case "interface":
                case "interfacename":
                    config.InterfaceName = value;
                    break;
                case "preferipv6":
                case "ipv6":
                    if (bool.TryParse(value, out var preferIpv6)) config.PreferIPv6 = preferIpv6;
                    break;
                case "role": config.Role = value; break;
                case "seed":
                    config.SeedNodes.Add(value);
                    break;
            }
        }

        return config;
    }
}

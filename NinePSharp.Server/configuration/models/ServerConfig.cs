using System.Collections.Generic;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Configuration.Models;

public class ServerConfig
{
    public List<EndpointConfig> Endpoints { get; set; } = new();
    
    // Cluster Configuration
    public AkkaConfig? Akka { get; set; }

    // Translation backends
    public RestBackendConfig? Rest { get; set; }
    public GrpcBackendConfig? Grpc { get; set; }
    public MqttBackendConfig? Mqtt { get; set; }
    public SoapBackendConfig? Soap { get; set; }
    public JsonRpcBackendConfig? JsonRpc { get; set; }
    public WebsocketBackendConfig? Websocket { get; set; }
    public DatabaseBackendConfig? Database { get; set; }
    public EthereumBackendConfig? Ethereum { get; set; }
    public BitcoinBackendConfig? Bitcoin { get; set; }
    public SolanaBackendConfig? Solana { get; set; }
    public StellarBackendConfig? Stellar { get; set; }
    public CardanoBackendConfig? Cardano { get; set; }
    public SecretBackendConfig? Secret { get; set; }
    public AwsBackendConfig? Aws { get; set; }
    public AzureBackendConfig? Azure { get; set; }
    public GcpBackendConfig? Gcp { get; set; }
}

public class AkkaConfig
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

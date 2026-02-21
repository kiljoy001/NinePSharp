using System.Collections.Generic;
using NinePSharp.Server.Configuration.Models;

namespace NinePSharp.Server.Configuration.Models;

public class ServerConfig
{
    public List<EndpointConfig> Endpoints { get; set; } = new();
    
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
}

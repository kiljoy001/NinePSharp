using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SharpFuzz;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Backends.MQTT;
using NinePSharp.Server.Backends.REST;
using NinePSharp.Server.Backends.gRPC;
using NinePSharp.Server.Backends.SOAP;
using NinePSharp.Server.Backends.Websockets;
using NinePSharp.Server.Backends.JsonRpc;
using NinePSharp.Backends.Compute;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using Moq;
using Moq.Protected;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;

namespace NinePSharp.Fuzzer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "rest")
            {
                FuzzRest();
            }
            else if (args.Length > 0 && args[0] == "soap")
            {
                FuzzSoap();
            }
            else if (args.Length > 0 && args[0] == "grpc")
            {
                FuzzGrpc();
            }
            else if (args.Length > 0 && args[0] == "db")
            {
                FuzzDatabase();
            }
            else if (args.Length > 0 && args[0] == "mqtt")
            {
                FuzzMqtt();
            }
            else if (args.Length > 0 && args[0] == "ws")
            {
                FuzzWebsocket();
            }
            else if (args.Length > 0 && args[0] == "jsonrpc")
            {
                FuzzJsonRpc();
            }
            else if (args.Length > 0 && args[0] == "compute")
            {
                FuzzCompute();
            }
            else if (args.Length > 0 && (args[0] == "blockchain" || args[0] == "chain"))
            {
                FuzzBlockchain();
            }
            else if (args.Length > 0 && args[0] == "mock")
            {
                FuzzMockFileSystem();
            }
            else if (args.Length > 0 && args[0] == "secret")
            {
                FuzzSecretFileSystem();
            }
            else
            {
                FuzzParser();
            }
        }

        private static void FuzzParser()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        NinePSharp.Parser.NinePParser.parse(true, data.AsMemory());
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzCompute()
        {
            var config = new ComputeBackendConfig { MountPath = "/compute" };
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new ComputeFileSystem(config);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();
                        fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzWebsocket()
        {
            var config = new WebsocketBackendConfig { Url = "ws://localhost", MountPath = "/ws" };
            var transportMock = new Mock<IWebsocketTransport>();
            transportMock.Setup(x => x.GetNextMessageAsync()).ReturnsAsync(Array.Empty<byte>());

            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new WebsocketFileSystem(config, transportMock.Object, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzMqtt()
        {
            var config = new MqttBackendConfig { BrokerUrl = "localhost", ClientId = "fuzz" };
            var transportMock = new Mock<IMqttTransport>();
            transportMock.Setup(x => x.GetNextMessageAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<byte>());

            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new MqttFileSystem(config, transportMock.Object, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzDatabase()
        {
            var config = new DatabaseBackendConfig { MountPath = "/db", ProviderName = "Mock", ConnectionString = "None" };
            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new DatabaseFileSystem(config, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzGrpc()
        {
            var config = new GrpcBackendConfig { Host = "localhost", Port = 50051 };
            var transportMock = new Mock<IGrpcTransport>();
            transportMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<IDictionary<string, string>>()))
                         .ReturnsAsync(Array.Empty<byte>());

            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new GrpcFileSystem(config, transportMock.Object, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzSoap()
        {
            var config = new SoapBackendConfig { WsdlUrl = "http://localhost?wsdl" };
            var transportMock = new Mock<ISoapTransport>();
            transportMock.Setup(x => x.CallActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                         .ReturnsAsync("<OK/>");

            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new SoapFileSystem(config, transportMock.Object, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzRest()
        {
            var config = new RestBackendConfig { BaseUrl = "http://localhost" };
            
            // Mock HttpClient to avoid actual network calls during fuzzing
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>(
                  "SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>()
               )
               .ReturnsAsync(new HttpResponseMessage()
               {
                  StatusCode = System.Net.HttpStatusCode.OK,
                  Content = new StringContent("{}"),
               });

            var client = new HttpClient(handlerMock.Object);
            var vault = new LuxVaultService();
            
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new RestFileSystem(config, client, vault);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        var text = System.Text.Encoding.UTF8.GetString(data);
                        
                        // Fuzz Walk logic with parts of the string
                        var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();

                        // Fuzz Header/Param parsing and Request Execution
                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                        
                        fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzJsonRpc()
        {
            var vault = new LuxVaultService();
            var transportMock = new Mock<IJsonRpcTransport>();
            transportMock.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<object?[]?>()))
                         .ReturnsAsync(JsonValue.Create("fuzz-result"));

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    if (data.Length < 5) return;

                    // Use first few bytes to seed the config
                    int endpointCount = (data[0] % 5) + 1;
                    var config = new JsonRpcBackendConfig { MountPath = "/rpc" };
                    for (int i = 0; i < endpointCount; i++)
                    {
                        config.Endpoints.Add(new JsonRpcEndpointConfig
                        {
                            Name = $"e{i}",
                            Path = (data[1] % 2 == 0) ? "a/b" : "c",
                            Method = "m",
                            Writable = (data[2] % 2 == 0)
                        });
                    }

                    var fs = new JsonRpcFileSystem(config, transportMock.Object);
                    var text = System.Text.Encoding.UTF8.GetString(data.Skip(3).ToArray());
                    var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);

                    // Stress the hierarchical logic
                    fs.WalkAsync(new Twalk(1, 0, 1, parts)).Wait();
                    fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    
                    if (parts.Length > 0)
                    {
                        var twrite = new Twrite(1, 1, 0, data);
                        fs.WriteAsync(twrite).Wait();
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzBlockchain()
        {
            var vault = new LuxVaultService();

            INinePFileSystem NewBitcoin() => new BitcoinFileSystem(new BitcoinBackendConfig { Network = "Main" }, null, vault);
            INinePFileSystem NewEthereum() => new EthereumFileSystem(new EthereumBackendConfig { RpcUrl = "http://localhost" }, null!, vault);
            INinePFileSystem NewSolana() => new SolanaFileSystem(new SolanaBackendConfig(), null, vault);
            INinePFileSystem NewStellar() => new StellarFileSystem(new StellarBackendConfig(), null, vault);
            INinePFileSystem NewCardano() => new CardanoFileSystem(new CardanoBackendConfig(), vault);

            var factories = new Func<INinePFileSystem>[]
            {
                NewBitcoin,
                NewEthereum,
                NewSolana,
                NewStellar,
                NewCardano
            };

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    var text = System.Text.Encoding.UTF8.GetString(data);

                    var fs = factories[(data.Length == 0 ? 0 : data[0]) % factories.Length]();
                    var pathParts = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);

                    fs.WalkAsync(new Twalk(1, 0, 1, pathParts)).Wait();
                    fs.OpenAsync(new Topen(1, 1, 0)).Wait();
                    fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();
                    fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    fs.StatAsync(new Tstat(1, 1)).Wait();

                    var clone = fs.Clone();
                    clone.StatAsync(new Tstat(1, 1)).Wait();
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }

        private static void FuzzMockFileSystem()
        {
            var vault = new LuxVaultService();

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0) return;

                        var fs = new MockFileSystem(vault);
                        var text = System.Text.Encoding.UTF8.GetString(data);

                        // Extract filename from fuzzed data
                        var fileName = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries)
                                           .FirstOrDefault() ?? "fuzzfile.txt";

                        // Fuzz StatfsAsync
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 1, 1)).Wait();

                        // Fuzz file creation
                        fs.LcreateAsync(new Tlcreate(100, 1, 1, fileName, 0, 0644, 0)).Wait();

                        // Fuzz directory creation
                        if (data.Length > 1 && data[1] % 2 == 0)
                        {
                            fs.MkdirAsync(new Tmkdir(100, 2, 1, fileName + "_dir", 0755, 0)).Wait();
                        }

                        // Fuzz walk
                        fs.WalkAsync(new Twalk(1, 1, 2, new[] { fileName })).Wait();

                        // Fuzz write/read
                        fs.WriteAsync(new Twrite(1, 2, 0, data)).Wait();
                        fs.ReadAsync(new Tread(1, 2, 0, (uint)Math.Min(data.Length, 8192))).Wait();

                        // Fuzz rename
                        if (data.Length > 2)
                        {
                            fs.RenameAsync(new Trename(100, 3, 2, 1, fileName + "_renamed")).Wait();
                        }

                        // Fuzz renameat
                        if (data.Length > 3)
                        {
                            fs.RenameatAsync(new Trenameat(100, 4, 1, fileName, 1, fileName + "_new")).Wait();
                        }

                        // Fuzz readdir
                        fs.ReaddirAsync(new Treaddir(100, 5, 1, 0, 8192)).Wait();

                        // Fuzz clone
                        var clone = fs.Clone();
                        ((MockFileSystem)clone).StatfsAsync(new Tstatfs(100, 1, 1)).Wait();
                    }
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }

        private static void FuzzSecretFileSystem()
        {
            var vault = new LuxVaultService();
            var config = new SecretBackendConfig();

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0) return;

                        var fs = new SecretFileSystem(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, config, vault);
                        var text = System.Text.Encoding.UTF8.GetString(data);

                        // Fuzz StatfsAsync at root
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 1, 1)).Wait();

                        // Fuzz ReaddirAsync at root
                        fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192)).Wait();

                        // Fuzz walk to vault
                        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();

                        // Fuzz StatfsAsync in vault
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 2, 2)).Wait();

                        // Fuzz ReaddirAsync in vault
                        fs.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192)).Wait();

                        // Fuzz walk to provision
                        fs.WalkAsync(new Twalk(2, 2, 3, new[] { "..", "provision" })).Wait();

                        // Fuzz write to provision (secret provisioning)
                        fs.WriteAsync(new Twrite(1, 3, 0, data)).Wait();

                        // Fuzz walk to unlock
                        fs.WalkAsync(new Twalk(3, 3, 4, new[] { "..", "unlock" })).Wait();

                        // Fuzz write to unlock
                        fs.WriteAsync(new Twrite(1, 4, 0, data)).Wait();

                        // Fuzz StatAsync
                        fs.StatAsync(new Tstat(1, 1)).Wait();

                        // Fuzz clone
                        var clone = fs.Clone();
                        ((SecretFileSystem)clone).StatfsAsync(new Tstatfs(100, 1, 1)).Wait();

                        // Fuzz readdir with different offsets
                        if (data.Length > 1)
                        {
                            var offset = (ulong)(data[1] % 100);
                            fs.ReaddirAsync(new Treaddir(100, 1, 1, offset, 8192)).Wait();
                        }
                    }
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }
    }
}

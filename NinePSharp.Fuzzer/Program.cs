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
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using Moq;
using Moq.Protected;

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
                // Note: Full DB fuzzing would need to shim the factory, 
                // here we fuzz the 9P logic layer.
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
    }
}

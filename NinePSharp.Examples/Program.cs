using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Examples;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.FSharp;

namespace NinePSharp.Examples;

class DummyAuthService : IEmercoinAuthService
{
    public Task<bool> IsCertificateAuthorizedAsync(X509Certificate2 certificate) => Task.FromResult(true);
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var logger = loggerFactory.CreateLogger("9P-Server");
        
        var handler = new InMemoryHandler();
        handler.AddDirectory("docs");
        handler.AddFile("docs/readme.txt", "This is an in-memory 9P server example.");
        handler.AddFile("hello.txt", "Hello from NinePSharp!");

        var dispatcherEngine = new NinePFSDispatcherEngine(handler);
        var dispatcher = new SimpleDispatcher(dispatcherEngine);

        var processor = new NinePConnectionProcessor(
            logger,
            dispatcher,
            new DummyAuthService()
        );

        var listener = new TcpListener(IPAddress.Any, 5640); // Use 5640 to avoid permission issues
        listener.Start();
        logger.LogInformation("9P Server listening on port 5640...");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Client connected: {RemoteEndPoint}", client.Client.RemoteEndPoint);
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var session = new NinePConnectionProcessor.ClientSession();
                        await processor.ProcessStreamAsync(stream, client.Client.RemoteEndPoint, session, CancellationToken.None);
                    }
                    logger.LogInformation("Client disconnected.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing connection.");
                }
            });
        }
    }
}

class SimpleDispatcher : INinePFSDispatcher
{
    private readonly INinePFSDispatcher _engine;
    public SimpleDispatcher(INinePFSDispatcher engine) => _engine = engine;
    public Task<object> DispatchAsync(string sessionId, NinePSharp.Parser.NinePMessage message, NinePSharp.Constants.NinePDialect dialect, X509Certificate2? certificate = null)
        => _engine.DispatchAsync(sessionId, message, dialect, certificate);
}

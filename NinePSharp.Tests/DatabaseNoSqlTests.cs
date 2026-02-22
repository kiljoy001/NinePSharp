using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class DatabaseNoSqlTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }

    [Fact]
    public async Task Database_NoSqlQuery_ReadsViaHttpEndpoint()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
        });

        var config = new DatabaseBackendConfig
        {
            MountPath = "/db",
            Username = "alice",
            Password = "secret",
            Queries =
            {
                new DatabaseQueryConfig { Name = "docs.json", Query = "{ find: 'docs' }" }
            },
            NoSql = new NoSqlHttpConfig
            {
                EndpointUrl = "http://127.0.0.1:8080/query",
                Method = "POST",
                QueryField = "query"
            }
        };

        var fs = new DatabaseFileSystem(
            config,
            new Mock<ILuxVaultService>().Object,
            credentials: null,
            queryExecutor: null,
            noSqlHandler: handler);

        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "docs.json" }));
        var read = await fs.ReadAsync(new Tread(2, 1, 0, 4096));
        string payload = Encoding.UTF8.GetString(read.Data.ToArray());

        Assert.Contains("\"ok\":true", payload);
        Assert.NotNull(handler.LastRequest);
        Assert.NotNull(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("{ find: 'docs' }", doc.RootElement.GetProperty("query").GetString());
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Server.Backends.JsonRpc;

namespace NinePSharp.Tests;

public class JsonRpcTransportTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responseFactory(request));
        }
    }

    [Fact]
    public async Task CallAsync_AcceptsBitcoinStyleEnvelope()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":{\"chain\":\"main\"},\"error\":null,\"id\":1}",
                Encoding.UTF8,
                "application/json")
        });

        var transport = new JsonRpcTransport("http://127.0.0.1:6662/", "test", "pass", handler);

        var result = await transport.CallAsync("getblockchaininfo");

        result.Should().NotBeNull();
        result!["chain"]!.GetValue<string>().Should().Be("main");
    }

    [Fact]
    public async Task CallAsync_SendsBasicAuthHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":true,\"error\":null,\"id\":1}",
                Encoding.UTF8,
                "application/json")
        });

        const string user = "alice";
        const string password = "secret";
        var transport = new JsonRpcTransport("http://127.0.0.1:6662/", user, password, handler);

        _ = await transport.CallAsync("ping");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password}"));
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(expected);
    }

    [Fact]
    public async Task CallAsync_WhenJsonRpcError_ThrowsWithCodeAndMessage()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"result\":null,\"error\":{\"code\":-8,\"message\":\"bad parameters\"},\"id\":1}",
                Encoding.UTF8,
                "application/json")
        });

        var transport = new JsonRpcTransport("http://127.0.0.1:6662/", "test", "pass", handler);

        Func<Task> act = async () => await transport.CallAsync("getblockhash");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("JSON-RPC error -8: bad parameters");
    }
}

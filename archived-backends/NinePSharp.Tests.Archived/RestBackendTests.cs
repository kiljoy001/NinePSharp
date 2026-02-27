using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.REST;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class RestBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly RestBackendConfig _config = new() { BaseUrl = "http://api.test" };

    private HttpClient CreateMockClient(Func<HttpRequestMessage, Task> verifyAction)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .Callback<HttpRequestMessage, CancellationToken>(async (req, token) => await verifyAction(req))
           .ReturnsAsync(new HttpResponseMessage()
           {
              StatusCode = System.Net.HttpStatusCode.OK,
              Content = new StringContent("{\"status\":\"ok\"}"),
           });

        return new HttpClient(handlerMock.Object) { BaseAddress = new Uri(_config.BaseUrl) };
    }

    [Fact]
    public async Task Rest_Get_Mapping_Works()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateMockClient(req => { capturedRequest = req; return Task.CompletedTask; });
        var fs = new RestFileSystem(_config, client, _vaultMock.Object);

        // Walk to /users/get
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "users", "get" }));
        
        // Act
        await fs.ReadAsync(new Tread(1, 1, 0, 8192));

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal("http://api.test/users", capturedRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task Rest_Headers_And_Params_Apply_To_Request()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateMockClient(req => { capturedRequest = req; return Task.CompletedTask; });
        var fs = new RestFileSystem(_config, client, _vaultMock.Object);

        // Walk to /api
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "api" }));
        
        // Walk to .headers and write
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { ".headers" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("X-Test: Value1\nContent-Type: text/plain")));

        // Return to /api and set query params
        await fs.WalkAsync(new Twalk(1, 2, 3, new[] { "..", ".params" }));
        await fs.WriteAsync(new Twrite(1, 3, 0, Encoding.UTF8.GetBytes("page=1\nlimit=50")));

        // Return to /api and walk to GET endpoint
        await fs.WalkAsync(new Twalk(1, 3, 4, new[] { "..", "get" }));
        
        // Act
        await fs.ReadAsync(new Tread(1, 4, 0, 8192));

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.Contains("X-Test"));
        Assert.Equal("Value1", capturedRequest.Headers.GetValues("X-Test").First());
        Assert.Contains("page=1", capturedRequest.RequestUri?.Query);
        Assert.Contains("limit=50", capturedRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task Rest_Post_Body_ZeroExposure_Works()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        
        var client = CreateMockClient(async req => {
            capturedRequest = req;
            if (req.Content != null) capturedBody = await req.Content.ReadAsStringAsync();
        });
        var fs = new RestFileSystem(_config, client, _vaultMock.Object);

        // Walk to /submit/post
        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "submit", "post" }));
        
        // Act
        var jsonBody = "{\"key\":\"value\"}";
        await fs.WriteAsync(new Twrite(1, 1, 0, Encoding.UTF8.GetBytes(jsonBody)));

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal(jsonBody, capturedBody);
    }
}

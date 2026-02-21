using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Moq.Protected;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Backends.REST;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Tests.Backends;

public class RestBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task RestBackend_Initialization_Works()
    {
        var backend = new RestBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("BaseUrl", "https://api.example.com"),
            new KeyValuePair<string, string?>("MountPath", "/rest")
        }).Build();

        await backend.InitializeAsync(config);
        backend.Name.Should().Be("REST");
        backend.MountPath.Should().Be("/rest");
    }

    [Fact]
    public async Task RestFileSystem_Read_PerformsGetRequest()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"id\": 1, \"name\": \"test\"}")
            })
            .Verifiable();

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("https://api.example.com/");
        
        var config = new RestBackendConfig { BaseUrl = "https://api.example.com" };
        var fs = new RestFileSystem(config, httpClient, _vault);

        // Act: Walk to 'users/1'
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "users", "1" }));
        var response = await fs.ReadAsync(new Tread((ushort)1, 2u, 0uL, 8192u));

        // Assert
        var content = Encoding.UTF8.GetString(response.Data.ToArray());
        content.Should().Contain("test");
        
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(1),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Get && 
                req.RequestUri!.ToString() == "https://api.example.com/users/1"),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task RestFileSystem_Write_PerformsPostRequest()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.Created, Content = new StringContent("{}") })
            .Verifiable();

        var httpClient = new HttpClient(handlerMock.Object);
        httpClient.BaseAddress = new Uri("https://api.example.com/");

        var config = new RestBackendConfig { BaseUrl = "https://api.example.com" };
        var fs = new RestFileSystem(config, httpClient, _vault);

        // Act: Walk to 'users' and write
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "users" }));
        var payload = "{\"name\": \"newuser\"}";
        await fs.WriteAsync(new Twrite((ushort)1, 2u, 0uL, Encoding.UTF8.GetBytes(payload).AsMemory()));

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(1),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post && 
                req.RequestUri!.ToString() == "https://api.example.com/users"),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task RestFileSystem_Auth_UsesSecurePipeline()
    {
        var backend = new RestBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("BaseUrl", "https://api.example.com")
        }).Build();
        await backend.InitializeAsync(config);
        
        using var securePass = new SecureString();
        foreach(var c in "user:pass") { securePass.AppendChar(c); }
        securePass.MakeReadOnly();

        var fs = backend.GetFileSystem(securePass);
        fs.Should().NotBeNull();
    }
}

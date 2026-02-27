using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class EmercoinAndBackendFuzzTests
{
    private static readonly X509Certificate2 Certificate = CreateCertificate();

    [Property(MaxTest = 25)]
    public void MockBackend_Initialize_Uses_Configured_MountPath_Or_Default(NonEmptyString mountSuffix, bool provideMountPath)
    {
        var backend = new MockBackend(new LuxVaultService());
        var clean = mountSuffix.Get.Replace("/", string.Empty).Replace("..", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "mock-alt";

        var configMap = new Dictionary<string, string?>();
        if (provideMountPath)
        {
            configMap["MountPath"] = "/" + clean;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(configMap).Build();
        backend.InitializeAsync(config).Sync();

        backend.MountPath.Should().Be(provideMountPath ? "/" + clean : "/mock");
        backend.GetFileSystem().Should().BeOfType<MockFileSystem>();
        backend.GetFileSystem(credentials: null).Should().BeOfType<MockFileSystem>();
    }

    [Property(MaxTest = 30)]
    public void EmercoinAuthService_Thumbprint_Then_Serial_Fallback(bool thumbprintExists, bool serialExists)
    {
        var nvs = new Mock<IEmercoinNvsClient>(MockBehavior.Strict);
        var thumb = Certificate.Thumbprint!.ToLowerInvariant();
        var serial = Certificate.SerialNumber.ToLowerInvariant();

        nvs.Setup(c => c.GetNameValueAsync($"ssl:{thumb}"))
            .ReturnsAsync(thumbprintExists ? "thumb-record" : null);
        nvs.Setup(c => c.GetNameValueAsync($"ssl:{serial}"))
            .ReturnsAsync(serialExists ? "serial-record" : null);

        var service = new EmercoinAuthService(nvs.Object, NullLogger<EmercoinAuthService>.Instance);
        var result = service.IsCertificateAuthorizedAsync(Certificate).Sync();

        result.Should().Be(thumbprintExists || serialExists);
        nvs.Verify(c => c.GetNameValueAsync($"ssl:{thumb}"), Times.Once());
        var serialLookupTimes = thumbprintExists ? Times.Never() : Times.Once();
        nvs.Verify(c => c.GetNameValueAsync($"ssl:{serial}"), serialLookupTimes);
    }

    [Fact]
    public async Task EmercoinAuthService_Null_Certificate_Is_Not_Authorized()
    {
        var nvs = new Mock<IEmercoinNvsClient>(MockBehavior.Strict);
        var service = new EmercoinAuthService(nvs.Object, NullLogger<EmercoinAuthService>.Instance);
        var authorized = await service.IsCertificateAuthorizedAsync(null!);
        authorized.Should().BeFalse();
    }

    [Property(MaxTest = 20)]
    public void EmercoinNvsClient_Auth_Header_Depends_On_Username(bool withUsername)
    {
        using var http = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":null}") }));

        var cfg = new EmercoinConfig
        {
            EndpointUrl = "http://localhost:6662/",
            Username = withUsername ? "alice" : "",
            Password = "pw"
        };

        _ = new EmercoinNvsClient(http, Options.Create(cfg), NullLogger<EmercoinNvsClient>.Instance);

        if (withUsername)
        {
            var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("alice:pw"));
            http.DefaultRequestHeaders.Authorization.Should().NotBeNull();
            http.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Basic");
            http.DefaultRequestHeaders.Authorization!.Parameter.Should().Be(expected);
        }
        else
        {
            http.DefaultRequestHeaders.Authorization.Should().BeNull();
        }
    }

    [Fact]
    public async Task EmercoinNvsClient_Parses_JsonRpc_Result_Value()
    {
        string? capturedBody = null;
        using var http = new HttpClient(new StubHttpHandler(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().Sync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":{\"value\":\"allow\"}}", Encoding.UTF8, "application/json")
            };
        }));

        var sut = new EmercoinNvsClient(
            http,
            Options.Create(new EmercoinConfig { EndpointUrl = "http://localhost:6662/" }),
            NullLogger<EmercoinNvsClient>.Instance);

        var value = await sut.GetNameValueAsync("ssl:abc123");

        value.Should().Be("allow");
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"method\":\"name_show\"");
        capturedBody.Should().Contain("ssl:abc123");
    }

    [Property(MaxTest = 25)]
    public void EmercoinNvsClient_NonSuccess_Status_Yields_Null(PositiveInt seed)
    {
        var code = 400 + (seed.Get % 200);
        var status = (HttpStatusCode)code;

        using var http = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(status) { Content = new StringContent("{}") }));

        var sut = new EmercoinNvsClient(
            http,
            Options.Create(new EmercoinConfig { EndpointUrl = "http://localhost:6662/" }),
            NullLogger<EmercoinNvsClient>.Instance);

        var value = sut.GetNameValueAsync("ssl:missing").Sync();
        value.Should().BeNull();
    }

    [Fact]
    public async Task EmercoinNvsClient_Fuzz_Malformed_Json_Does_Not_Throw()
    {
        var random = new Random(1337);

        for (int i = 0; i < 120; i++)
        {
            int len = random.Next(0, 160);
            var bytes = new byte[len];
            random.NextBytes(bytes);
            var payload = Encoding.UTF8.GetString(bytes);

            using var http = new HttpClient(new StubHttpHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                }));

            var sut = new EmercoinNvsClient(
                http,
                Options.Create(new EmercoinConfig { EndpointUrl = "http://localhost:6662/" }),
                NullLogger<EmercoinNvsClient>.Instance);

            Func<Task> act = async () => _ = await sut.GetNameValueAsync($"ssl:fuzz-{i}");
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task EmercoinNvsClient_Transport_Exception_Is_Handled_As_Null()
    {
        using var http = new HttpClient(new ThrowingHttpHandler());
        var sut = new EmercoinNvsClient(
            http,
            Options.Create(new EmercoinConfig { EndpointUrl = "http://localhost:6662/" }),
            NullLogger<EmercoinNvsClient>.Instance);

        var result = await sut.GetNameValueAsync("ssl:anything");
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessHardening_Apply_Emits_Expected_Result_Message_On_Linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            ProcessHardening.Apply();
            return;
        }

        int expected = NativeMethods.prctl(NativeMethods.PR_SET_DUMPABLE, 0, 0, 0, 0);

        using var writer = new System.IO.StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(writer);
        try
        {
            ProcessHardening.Apply();
        }
        finally
        {
            Console.SetOut(oldOut);
        }

        var output = writer.ToString();
        if (expected == 0)
        {
            output.Should().Contain("Anti-Dumping protection enabled");
        }
        else
        {
            output.Should().Contain("Failed to set PR_SET_DUMPABLE");
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ninepsharp-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("simulated transport failure");
        }
    }
}

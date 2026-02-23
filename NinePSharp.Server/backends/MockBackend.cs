using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class MockBackend : IProtocolBackend
{
    private readonly ILuxVaultService _vault;

    public MockBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public string Name => "Mock";
    public string MountPath { get; private set; } = "/mock";

    public Task InitializeAsync(IConfiguration configuration)
    {
        MountPath = configuration["MountPath"] ?? MountPath;
        Console.WriteLine($"[Mock Backend] Initialized with MountPath: {MountPath}");
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => new MockFileSystem(_vault);
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}

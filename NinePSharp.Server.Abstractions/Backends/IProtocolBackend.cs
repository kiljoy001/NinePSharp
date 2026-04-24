using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NinePSharp.Server.Interfaces;

public interface IProtocolBackend
{
    string Name { get; }
    string MountPath { get; }
    Task InitializeAsync(IConfiguration configuration);
    INinePFileSystem GetFileSystem(X509Certificate2? certificate = null);
    INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null);
}

public interface INinePFileSystem : IBackendRuntime
{
    // High-level filesystem interface, can add more methods if needed.
}

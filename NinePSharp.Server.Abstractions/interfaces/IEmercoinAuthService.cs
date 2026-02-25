using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Service for validating client certificates against the Emercoin blockchain.
/// </summary>
public interface IEmercoinAuthService
{
    /// <summary>
    /// Checks if the provided certificate is authorized based on Emercoin NVS records.
    /// </summary>
    /// <param name="certificate">The client certificate to validate.</param>
    /// <returns>True if authorized, false otherwise.</returns>
    Task<bool> IsCertificateAuthorizedAsync(X509Certificate2 certificate);
}

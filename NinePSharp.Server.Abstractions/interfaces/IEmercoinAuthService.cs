using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

public interface IEmercoinAuthService
{
    Task<bool> IsCertificateAuthorizedAsync(X509Certificate2 certificate);
}

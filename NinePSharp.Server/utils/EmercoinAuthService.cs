using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Utils;

public class EmercoinAuthService : IEmercoinAuthService
{
    private readonly IEmercoinNvsClient _nvsClient;
    private readonly ILogger<EmercoinAuthService> _logger;

    public EmercoinAuthService(IEmercoinNvsClient nvsClient, ILogger<EmercoinAuthService> logger)
    {
        _nvsClient = nvsClient;
        _logger = logger;
    }

    public async Task<bool> IsCertificateAuthorizedAsync(X509Certificate2 certificate)
    {
        if (certificate == null) return false;

        // Extract the thumbprint or serial number - Emercoin NVS records for SSL 
        // usually use the name "ssl:<serial_number_or_thumbprint>"
        string thumbprint = certificate.Thumbprint.ToLowerInvariant();
        string serialNumber = certificate.SerialNumber.ToLowerInvariant();

        _logger.LogInformation("Verifying certificate in Emercoin NVS. Thumbprint: {Thumbprint}, Serial: {Serial}", thumbprint, serialNumber);

        // Check thumbprint record
        var record = await _nvsClient.GetNameValueAsync($"ssl:{thumbprint}");
        if (record != null)
        {
            _logger.LogInformation("Authorized via Emercoin NVS (thumbprint match).");
            return true;
        }

        // Check serial number record
        record = await _nvsClient.GetNameValueAsync($"ssl:{serialNumber}");
        if (record != null)
        {
            _logger.LogInformation("Authorized via Emercoin NVS (serial match).");
            return true;
        }

        _logger.LogWarning("Certificate NOT found in Emercoin NVS.");
        return false;
    }
}

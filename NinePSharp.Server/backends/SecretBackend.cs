using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class SecretFileSystem : INinePFileSystem
{
    private readonly SecretBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private Dictionary<string, byte[]> _decryptedSecrets = new();

    public SecretFileSystem(SecretBackendConfig config, ILuxVaultService vault)
    {
        _config = config;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (_currentPath.Count > 0) _currentPath.RemoveAt(_currentPath.Count - 1);
            }
            else if (name != ".")
            {
                _currentPath.Add(name);
            }
            
            qids.Add(GetQid());
        }
        
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true; // root
        if (path[0] == "provision" && path.Count == 1) return false;
        if (path[0] == "unlock" && path.Count == 1) return false;
        if (path[0] == "vault" && path.Count == 1) return true;
        return false;
    }

    private Qid GetQid()
    {
        var type = IsDirectory(_currentPath) ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", _currentPath);
        ulong hash = (ulong)pathStr.GetHashCode();
        
        return new Qid(type, 0, hash);
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        return new Ropen(topen.Tag, GetQid(), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset > 0) return new Rread(tread.Tag, Array.Empty<byte>());

        string result = "";

        if (_currentPath.Count == 0)
        {
            result = "provision\nunlock\nvault\n";
        }
        else if (_currentPath[0] == "vault")
        {
            if (_currentPath.Count == 1)
            {
                result = string.Join("\n", _decryptedSecrets.Keys) + "\n";
            }
            else if (_currentPath.Count == 2)
            {
                if (_decryptedSecrets.TryGetValue(_currentPath[1], out var data))
                {
                    return new Rread(tread.Tag, data);
                }
                result = "Secret not found or locked.\n";
            }
        }

        return new Rread(tread.Tag, Encoding.UTF8.GetBytes(result));
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        var input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
        
        if (_currentPath.Count == 1)
        {
            if (_currentPath[0] == "provision")
            {
                // Format: password:name:payload
                var parts = input.Split(':', 3);
                if (parts.Length == 3)
                {
                    var password = parts[0];
                    var name = parts[1];
                    var payload = Encoding.UTF8.GetBytes(parts[2]);
                    
                    var encrypted = _vault.Encrypt(payload, password);
                    
                    // Generate hidden ID deterministically from password and name
                    var seed = _vault.DeriveSeed(password, Encoding.UTF8.GetBytes(name));
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes($"secret_{hiddenId}.vlt", encrypted);
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
            }
            else if (_currentPath[0] == "unlock")
            {
                // Format: password (unlocks everything it can find) 
                // OR password:name (direct lookup)
                var parts = input.Split(':', 2);
                var password = parts[0];
                
                if (parts.Length == 2)
                {
                    var name = parts[1];
                    var seed = _vault.DeriveSeed(password, Encoding.UTF8.GetBytes(name));
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    var vaultFile = $"secret_{hiddenId}.vlt";
                    
                    if (File.Exists(vaultFile))
                    {
                        var data = File.ReadAllBytes(vaultFile);
                        var decrypted = _vault.DecryptToBytes(data, password);
                        if (decrypted != null) _decryptedSecrets[name] = decrypted;
                    }
                }
                else
                {
                    // Legacy/Bulk unlock: still requires searching as names aren't known
                    var vaultFiles = Directory.GetFiles(".", "secret_*.vlt");
                    foreach (var vaultFile in vaultFiles)
                    {
                        try
                        {
                            var data = File.ReadAllBytes(vaultFile);
                            var decrypted = _vault.DecryptToBytes(data, password);
                            if (decrypted != null)
                            {
                                var id = Path.GetFileNameWithoutExtension(vaultFile).Replace("secret_", "");
                                _decryptedSecrets[id] = decrypted;
                            }
                        }
                        catch { }
                    }
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "secrets";
        var isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SecretFileSystem(_config, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._decryptedSecrets = new Dictionary<string, byte[]>(_decryptedSecrets);
        return clone;
    }
}

public class SecretBackend : IProtocolBackend
{
    private SecretBackendConfig? _config;
    private readonly ILuxVaultService _vault;

    public string Name => "secret";
    public string MountPath => "/secrets";

    public SecretBackend(ILuxVaultService vault)
    {
        _vault = vault;
    }

    public SecretBackend(SecretBackendConfig config, ILuxVaultService vault)
    {
        _config = config;
        _vault = vault;
    }

    public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _config = new SecretBackendConfig();
        configuration.Bind(_config);
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem() => new SecretFileSystem(_config ?? new SecretBackendConfig(), _vault);
}

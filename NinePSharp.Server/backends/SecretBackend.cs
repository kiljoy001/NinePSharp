using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class SecretFileSystem : INinePFileSystem
{
    private readonly SecretBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private Dictionary<string, ProtectedSecret> _decryptedSecrets = new();

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
            else
            {
                _currentPath.Add(name);
            }
            qids.Add(GetQid());
        }
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
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

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, GetQid(), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[]? allData = null;

        switch ((_currentPath.Count, _currentPath.ElementAtOrDefault(0)))
        {
            case (0, _):
                allData = Encoding.UTF8.GetBytes("provision\nunlock\nvault\n");
                break;

            case (1, "vault"):
                allData = Encoding.UTF8.GetBytes(string.Join("\n", _decryptedSecrets.Keys) + "\n");
                break;

            case (2, "vault"):
                if (_decryptedSecrets.TryGetValue(_currentPath[1], out var protectedSecret))
                {
                    string? revealed = protectedSecret.Reveal();
                    allData = revealed != null ? Encoding.UTF8.GetBytes(revealed) : Array.Empty<byte>();
                }
                else {
                    allData = Encoding.UTF8.GetBytes("Secret not found or locked.\n");
                }
                break;

            default:
                return new Rread(tread.Tag, Array.Empty<byte>());
        }

        if (allData == null) return new Rread(tread.Tag, Array.Empty<byte>());
        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();

        switch ((_currentPath.Count, _currentPath.ElementAtOrDefault(0)))
        {
            case (1, "provision"):
            {
                var parts = input.Split(':', 3);
                if (parts.Length == 3)
                {
                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();
                    
                    var name = parts[1];
                    var payload = Encoding.UTF8.GetBytes(parts[2]);

                    var encrypted = _vault.Encrypt(payload, password);
                    var seed = _vault.DeriveSeed(password, Encoding.UTF8.GetBytes(name));
                    var hiddenId = _vault.GenerateHiddenId(seed);

                    File.WriteAllBytes($"secret_{hiddenId}.vlt", encrypted);
                }
                break;
            }

            case (1, "unlock"):
            {
                var parts = input.Split(':', 2);
                using var password = new SecureString();
                foreach (char c in parts[0]) password.AppendChar(c);
                password.MakeReadOnly();

                switch (parts.Length)
                {
                    case 2:
                    {
                        var name = parts[1];
                        var seed = _vault.DeriveSeed(password, Encoding.UTF8.GetBytes(name));
                        var hiddenId = _vault.GenerateHiddenId(seed);
                        var vaultFile = $"secret_{hiddenId}.vlt";

                        if (File.Exists(vaultFile))
                        {
                            var data = File.ReadAllBytes(vaultFile);
                            var decrypted = _vault.Decrypt(data, password);
                            if (decrypted != null) _decryptedSecrets[name] = new ProtectedSecret(decrypted);
                        }
                        break;
                    }

                    default:
                    {
                        foreach (var vaultFile in Directory.GetFiles(".", "secret_*.vlt"))
                        {
                            try
                            {
                                var data = File.ReadAllBytes(vaultFile);
                                var decrypted = _vault.Decrypt(data, password);
                                if (decrypted != null)
                                {
                                    var id = Path.GetFileNameWithoutExtension(vaultFile).Replace("secret_", "");
                                    _decryptedSecrets[id] = new ProtectedSecret(decrypted);
                                }
                            }
                            catch { }
                        }
                        break;
                    }
                }
                break;
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
        clone._decryptedSecrets = _decryptedSecrets;
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

    public Task InitializeAsync(IConfiguration configuration)
    {
        _config = new SecretBackendConfig();
        configuration.Bind(_config);
        return Task.CompletedTask;
    }

    public INinePFileSystem GetFileSystem() => new SecretFileSystem(_config ?? new SecretBackendConfig(), _vault);
    public INinePFileSystem GetFileSystem(SecureString? credentials) => GetFileSystem();
}

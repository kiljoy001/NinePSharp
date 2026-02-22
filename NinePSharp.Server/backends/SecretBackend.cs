using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
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

    public bool DotU { get; set; }

    public SecretFileSystem(SecretBackendConfig config, ILuxVaultService vault)
    {
        _config = config;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);
        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                tempPath.Add(name);
            }
            qids.Add(GetQid(tempPath));
        }
        if (qids.Count == twalk.Wname.Length) _currentPath = tempPath;
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0] == "vault" && path.Count == 1) return true;
        return false;
    }

    private Qid GetQid(List<string> path)
    {
        var type = IsDirectory(path) ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", path);
        ulong hash = (ulong)pathStr.GetHashCode();
        return new Qid(type, 0, hash);
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, GetQid(_currentPath), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[] allData;

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type)>();

            if (_currentPath.Count == 0)
            {
                files.Add(("provision", QidType.QTFILE));
                files.Add(("unlock", QidType.QTFILE));
                files.Add(("vault", QidType.QTDIR));
            }
            else if (_currentPath[0] == "vault")
            {
                foreach (var s in _decryptedSecrets.Keys) files.Add((s, QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0600;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dotu: DotU);
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }
            allData = entries.ToArray();

            int totalToSend = 0;
            int curOff = (int)tread.Offset;
            while (curOff + 2 <= allData.Length)
            {
                ushort sz = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(curOff, 2)) + 2);
                if (totalToSend + sz > tread.Count) break;
                totalToSend += sz;
                curOff += sz;
            }
            
            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }
        else if (_currentPath.Count == 2 && _currentPath[0] == "vault")
        {
            byte[]? secretBytes = null;
            if (_decryptedSecrets.TryGetValue(_currentPath[1], out var protectedSecret))
            {
                protectedSecret.Use(bytes => secretBytes = bytes.ToArray());
            }
            
            if (secretBytes == null) return new Rread(tread.Tag, Array.Empty<byte>());
            
            try {
                if (tread.Offset >= (ulong)secretBytes.Length) return new Rread(tread.Tag, Array.Empty<byte>());
                allData = secretBytes.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)secretBytes.Length - (long)tread.Offset)).ToArray();
                return new Rread(tread.Tag, allData);
            }
            finally {
                if (secretBytes != null) Array.Clear(secretBytes);
            }
        }
        else {
            allData = Array.Empty<byte>();
        }

        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        var bytes = twrite.Data.Span;
        char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
        try {
            Encoding.UTF8.GetChars(bytes, chars);
            string input = new string(chars).Trim();

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

                        try {
                            var encrypted = _vault.Encrypt(payload, password);
                            var seed = _vault.DeriveSeed(password, Encoding.UTF8.GetBytes(name));
                            var hiddenId = _vault.GenerateHiddenId(seed);

                            File.WriteAllBytes($"secret_{hiddenId}.vlt", encrypted);
                        }
                        finally {
                            Array.Clear(payload);
                        }
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
                                var decrypted = _vault.DecryptToBytes(data, password);
                                if (decrypted != null) {
                                    try {
                                        _decryptedSecrets[name] = new ProtectedSecret((ReadOnlySpan<byte>)decrypted);
                                    }
                                    finally {
                                        Array.Clear(decrypted);
                                    }
                                }
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
                                    var decrypted = _vault.DecryptToBytes(data, password);
                                    if (decrypted != null)
                                    {
                                        try {
                                            var id = Path.GetFileNameWithoutExtension(vaultFile).Replace("secret_", "");
                                            _decryptedSecrets[id] = new ProtectedSecret((ReadOnlySpan<byte>)decrypted);
                                        }
                                        finally {
                                            Array.Clear(decrypted);
                                        }
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
        }
        finally {
            Array.Clear(chars);
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "secrets";
        var isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SecretFileSystem(_config, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._decryptedSecrets = _decryptedSecrets;
        clone.DotU = DotU;
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

    public async Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = new SecretBackendConfig();
            configuration.Bind(_config);
            Console.WriteLine($"[Secret Backend] Initialized with MountPath: {MountPath}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[Secret Backend] Failed to initialize: {ex.Message}");
        }
    }

    public INinePFileSystem GetFileSystem() => new SecretFileSystem(_config ?? new SecretBackendConfig(), _vault);
    public INinePFileSystem GetFileSystem(SecureString? credentials) => GetFileSystem();
}

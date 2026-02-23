using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger _logger;
    private readonly SecretBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    // Session passwords are shared across CLI-style short-lived connections.
    private static readonly ConcurrentDictionary<string, ProtectedSecret> _sessionPasswords = new();

    public bool DotU { get; set; }

    public SecretFileSystem(ILogger logger, SecretBackendConfig config, ILuxVaultService vault)
    {
        _logger = logger;
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
                foreach (var s in _sessionPasswords.Keys) files.Add((s, QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0600;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dotu: DotU);
                
                var entryBuffer = new byte[stat.Size + 2];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }

            var allData = entries.ToArray();
            if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
            var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
            return new Rread(tread.Tag, chunk);
        }

        if (_currentPath.Count == 2 && _currentPath[0] == "vault")
        {
            var secretName = _currentPath[1];
            byte[]? secretBytes = null;

            if (_sessionPasswords.TryGetValue(secretName, out var protectedPassword))
            {
                protectedPassword.Use(passwordBytes => {
                    using var secret = LuxVault.LoadSecret(secretName, passwordBytes);
                    if (secret != null)
                    {
                        secretBytes = secret.Span.ToArray();
                    }
                });
            }

            if (secretBytes == null) return new Rread(tread.Tag, Array.Empty<byte>());

            try
            {
                if (tread.Offset >= (ulong)secretBytes.Length) return new Rread(tread.Tag, Array.Empty<byte>());
                var chunk = secretBytes.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)secretBytes.Length - (long)tread.Offset)).ToArray();
                return new Rread(tread.Tag, chunk);
            }
            finally
            {
                Array.Clear(secretBytes);
            }
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
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
                    var parts = input.Split(':').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 3)
                    {
                        using var password = new SecureString();
                        foreach (char c in parts[0]) password.AppendChar(c);
                        password.MakeReadOnly();
                        
                        var name = parts[1];
                        var payload = Encoding.UTF8.GetBytes(parts[2]);

                        try
                        {
                            LuxVault.StoreSecret(name, payload, password);
                            _logger.LogInformation("[Secret FS] Provisioned '{Name}' to physical vault.", name);
                        }
                        finally
                        {
                            Array.Clear(payload);
                        }
                    }
                    break;
                }

                case (1, "unlock"):
                {
                    var parts = input.Split(':').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 2)
                    {
                        var name = parts[1];
                        using var password = new SecureString();
                        foreach (char c in parts[0]) password.AppendChar(c);
                        password.MakeReadOnly();

                        using var decrypted = LuxVault.LoadSecret(name, password);
                        if (decrypted != null)
                        {
                            var next = new ProtectedSecret(password);
                            _sessionPasswords.AddOrUpdate(
                                name,
                                _ => next,
                                (_, existing) => {
                                    existing.Dispose();
                                    return next;
                                });
                            _logger.LogInformation("[Secret FS] Unlocked '{Name}'. Password stored in session.", name);
                        }
                        else
                        {
                            _logger.LogWarning("[Secret FS] Failed to unlock '{Name}' - incorrect password or missing file.", name);
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

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var qid = new Qid(QidType.QTFILE, 0, (ulong)"secrets".GetHashCode());
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, 0600, 0, 0, 1, 0, 0, 4096, 0, now, 0, now, 0, now, 0, 0, 0, 0, 0));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SecretFileSystem(_logger, _config, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = DotU;
        return clone;
    }
}

public class SecretBackend : IProtocolBackend
{
    private SecretBackendConfig? _config;
    private readonly ILuxVaultService _vault;
    private readonly ILoggerFactory _loggerFactory;
    private SecretFileSystem? _prototypeFs;

    public string Name => "secret";
    public string MountPath => "/secrets";

    public SecretBackend(ILuxVaultService vault) : this(vault, NullLoggerFactory.Instance)
    {
    }

    public SecretBackend(ILuxVaultService vault, ILoggerFactory loggerFactory)
    {
        _vault = vault;
        _loggerFactory = loggerFactory;
    }

    public async Task InitializeAsync(IConfiguration configuration)
    {
        try {
            _config = new SecretBackendConfig();
            configuration.Bind(_config);
            _prototypeFs = new SecretFileSystem(_loggerFactory.CreateLogger<SecretFileSystem>(), _config, _vault);
            Console.WriteLine($"[Secret Backend] Initialized with MountPath: {MountPath}");
        }
        catch (Exception ex) {
            Console.WriteLine($"[Secret Backend] Failed to initialize: {ex.Message}");
        }
    }

    public INinePFileSystem GetFileSystem() => (_prototypeFs ?? throw new InvalidOperationException("Secret backend not initialized.")).Clone();
    public INinePFileSystem GetFileSystem(SecureString? credentials) => GetFileSystem();
}

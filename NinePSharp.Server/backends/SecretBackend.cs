using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
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
using NinePSharp.Parser;

namespace NinePSharp.Server.Backends;

public class SecretFileSystem : INinePFileSystem
{
    private readonly ILogger _logger;
    private readonly SecretBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    // Session passwords are shared across CLI-style short-lived connections.
    private static readonly ConcurrentDictionary<string, ProtectedSecret> _sessionPasswords = new();

    /// <summary>
    /// Clears all session passwords. Used for unit testing.
    /// </summary>
    public static void ClearSessionPasswords()
    {
        foreach (var secret in _sessionPasswords.Values) secret.Dispose();
        _sessionPasswords.Clear();
    }

    public NinePDialect Dialect { get; set; }

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
        // Use stackalloc for string bytes to avoid heap allocation for small paths
        Span<byte> pathBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(pathStr.Length)];
        int bytesWritten = Encoding.UTF8.GetBytes(pathStr, pathBytes);
        
        // Hash the path bytes
        ulong hash = (ulong)pathStr.GetHashCode(); // Keep standard hash for now
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
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dialect: Dialect);
                
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
        int maxChars = Encoding.UTF8.GetMaxCharCount(bytes.Length);
        char[] chars = System.Buffers.ArrayPool<char>.Shared.Rent(maxChars);
        try {
            int charsDecoded = Encoding.UTF8.GetChars(bytes, chars);
            var charSpan = chars.AsSpan(0, charsDecoded).Trim();

            switch ((_currentPath.Count, _currentPath.ElementAtOrDefault(0)))
            {
                case (1, "provision"):
                {
                    // Format: password:name:data
                    int firstColon = charSpan.IndexOf(':');
                    if (firstColon != -1)
                    {
                        var secondColonRelative = charSpan.Slice(firstColon + 1).IndexOf(':');
                        if (secondColonRelative != -1)
                        {
                            int secondColon = firstColon + 1 + secondColonRelative;
                            var passwordSpan = charSpan.Slice(0, firstColon).Trim();
                            var nameSpan = charSpan.Slice(firstColon + 1, secondColon - firstColon - 1).Trim();
                            var dataSpan = charSpan.Slice(secondColon + 1).Trim();

                            using var password = new SecureString();
                            foreach (char c in passwordSpan) password.AppendChar(c);
                            password.MakeReadOnly();
                            
                            string name = nameSpan.ToString();
                            
                            // Decode dataSpan to pinned byte array
                            int payloadByteCount = Encoding.UTF8.GetByteCount(dataSpan);
                            byte[] payload = GC.AllocateArray<byte>(payloadByteCount, pinned: true);
                            try
                            {
                                Encoding.UTF8.GetBytes(dataSpan, payload);
                                _vault.StoreSecret(name, payload, password);
                                _logger.LogInformation("[Secret FS] Provisioned '{Name}' to physical vault.", name);
                            }
                            finally
                            {
                                Array.Clear(payload);
                            }
                        }
                    }
                    break;
                }

                case (1, "unlock"):
                {
                    // Format: password:name
                    int colon = charSpan.IndexOf(':');
                    if (colon != -1)
                    {
                        var passwordSpan = charSpan.Slice(0, colon).Trim();
                        var nameSpan = charSpan.Slice(colon + 1).Trim();
                        
                        using var password = new SecureString();
                        foreach (char c in passwordSpan) password.AppendChar(c);
                        password.MakeReadOnly();

                        string name = nameSpan.ToString();
                        using var decrypted = _vault.LoadSecret(name, password);
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
            System.Buffers.ArrayPool<char>.Shared.Return(chars);
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "secrets";
        var isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott", dialect: Dialect);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public async Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count == 2 && _currentPath[0] == "vault")
        {
            var secretName = _currentPath[1];
            if (_sessionPasswords.TryRemove(secretName, out var protectedSecret))
            {
                protectedSecret.Dispose();
                return new Rremove(tremove.Tag);
            }
        }
        throw new NinePNotSupportedException("Only session passwords in vault/ can be removed.");
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var isDir = IsDirectory(_currentPath);
        var qid = GetQid(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0600;
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode, 0, 0, 1, 0, 0, 4096, 0, now, 0, now, 0, now, 0, 0, 0, 0, 0));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public Task<Rstatfs> StatfsAsync(Tstatfs tstatfs)
    {
        // Calculate number of files in the virtual filesystem
        ulong fileCount = 3; // root has: provision, unlock, vault

        if (_currentPath.Count >= 1 && _currentPath[0] == "vault")
        {
            fileCount = (ulong)_sessionPasswords.Count;
        }

        var totalFiles = fileCount + 1; // +1 for the directory itself
        var statfs = new Rstatfs(
            tag: tstatfs.Tag,
            fsType: 0x01021997, // 9P magic number
            bSize: 4096,
            blocks: 100000,
            bFree: 95000,
            bAvail: 95000,
            files: totalFiles,
            fFree: 1000000,
            fsId: (ulong)"secrets".GetHashCode(),
            nameLen: 255
        );
        return Task.FromResult(statfs);
    }

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        if (!IsDirectory(_currentPath))
        {
            throw new NinePProtocolException("Not a directory");
        }

        var entries = new List<byte>();
        var files = new List<(string Name, QidType Type)>();

        // Build file list based on current path
        if (_currentPath.Count == 0)
        {
            files.Add(("provision", QidType.QTFILE));
            files.Add(("unlock", QidType.QTFILE));
            files.Add(("vault", QidType.QTDIR));
        }
        else if (_currentPath[0] == "vault")
        {
            foreach (var s in _sessionPasswords.Keys)
            {
                files.Add((s, QidType.QTFILE));
            }
        }

        // Serialize each directory entry
        ulong offset = 0;
        Span<byte> qidBuffer = stackalloc byte[13];

        foreach (var (index, f) in files.Select((f, i) => (i, f)))
        {
            int nameByteCount = Encoding.UTF8.GetByteCount(f.Name);
            ulong entrySize = (ulong)(13 + 8 + 1 + 2 + nameByteCount);

            if (offset < treaddir.Offset)
            {
                offset += entrySize;
                continue;
            }

            // Check if this entry would exceed the count limit
            if ((ulong)entries.Count + entrySize > (ulong)treaddir.Count)
            {
                break;
            }

            // Write qid (13 bytes)
            qidBuffer[0] = (byte)f.Type;
            BitConverter.TryWriteBytes(qidBuffer.Slice(1, 4), 0); // Version
            BitConverter.TryWriteBytes(qidBuffer.Slice(5, 8), (ulong)f.Name.GetHashCode()); // Path
            foreach (var b in qidBuffer) entries.Add(b);

            // Write offset (8 bytes) - next entry's offset
            offset += entrySize;
            Span<byte> offsetBytes = stackalloc byte[8];
            BitConverter.TryWriteBytes(offsetBytes, offset);
            foreach (var b in offsetBytes) entries.Add(b);

            // Write type (1 byte)
            entries.Add(f.Type == QidType.QTDIR ? (byte)0x80 : (byte)0);

            // Write name (2 byte length + string)
            Span<byte> nameLenBytes = stackalloc byte[2];
            BitConverter.TryWriteBytes(nameLenBytes, (ushort)nameByteCount);
            foreach (var b in nameLenBytes) entries.Add(b);
            
            byte[] nameBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(nameByteCount);
            try {
                Encoding.UTF8.GetBytes(f.Name, nameBytes);
                for (int i = 0; i < nameByteCount; i++) entries.Add(nameBytes[i]);
            }
            finally {
                System.Buffers.ArrayPool<byte>.Shared.Return(nameBytes);
            }
        }

        var data = entries.ToArray();
        return Task.FromResult(new Rreaddir(
            size: (uint)(NinePConstants.HeaderSize + 4 + data.Length),
            tag: treaddir.Tag,
            count: (uint)data.Length,
            data: data
        ));
    }

    public INinePFileSystem Clone()
    {
        var clone = new SecretFileSystem(_logger, _config, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.Dialect = Dialect;
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

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => (_prototypeFs ?? throw new InvalidOperationException("Secret backend not initialized.")).Clone();
    public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => GetFileSystem(certificate);
}

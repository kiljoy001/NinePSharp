using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using CardanoSharp.Wallet;
using CardanoSharp.Wallet.Enums;
using CardanoSharp.Wallet.Models.Keys;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class CardanoFileSystem : INinePFileSystem
{
    private readonly CardanoBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedMnemonic;
    private string? _unlockedAddress;

    public bool DotU { get; set; }

    public CardanoFileSystem(CardanoBackendConfig config, ILuxVaultService vault)
    {
        _config = config;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0] == "wallets" && path.Count == 1) return true;
        return false;
    }

    private Qid GetQid(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", path);
        return new Qid(type, 0, (ulong)pathStr.GetHashCode());
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

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
            qids.Add(new Qid(IsDirectory(tempPath) ? QidType.QTDIR : QidType.QTFILE, 0, (ulong)name.GetHashCode()));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
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
                files.Add(("wallets", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "wallets")
            {
                files.Add(("create", QidType.QTFILE));
                files.Add(("import", QidType.QTFILE));
                files.Add(("unlock", QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                if (f.Name == "create" || f.Name == "import" || f.Name == "unlock") mode = 0666;
                
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }
            allData = entries.ToArray();
        }
        else
        {
            string result = "";
            var last = _currentPath.Last().ToLowerInvariant();
            switch (last)
            {
                case "unlock":
                    result = _unlockedAddress != null ? $"Unlocked: {_unlockedAddress}\n" : "Locked\n";
                    break;
                case "status":
                    result = $"Network: {_config.Network}\nAddress: {_unlockedAddress ?? "None"}\n";
                    break;
            }
            allData = Encoding.UTF8.GetBytes(result);
        }

        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 2 && _currentPath[0] == "wallets")
        {
            if (_currentPath[1] == "create")
            {
                var bytes = twrite.Data.Span;
                if (bytes.Length == 0) throw new NinePProtocolException("Password is required for wallet creation.");

                using var password = new SecureString();
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    foreach (char c in chars) if (c != '\n' && c != '\r') password.AppendChar(c);
                }
                finally {
                    Array.Clear(chars);
                }
                password.MakeReadOnly();

                var mnemonicService = new MnemonicService();
                var mnemonic = mnemonicService.Generate(24);
                byte[] wordsBytes = Encoding.UTF8.GetBytes(mnemonic.Words);
                
                try {
                    var ciphertext = _vault.Encrypt(wordsBytes, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"ada_vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(wordsBytes);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:mnemonicWords
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim();
                    var parts = fullStr.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:mnemonicWords'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var mnemonicWords = parts[1];
                    byte[] wordsBytes = Encoding.UTF8.GetBytes(mnemonicWords);
                    try {
                        var ciphertext = _vault.Encrypt(wordsBytes, password);
                        byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                        var seed = _vault.DeriveSeed(password, idSalt);
                        var hiddenId = _vault.GenerateHiddenId(seed);
                        
                        File.WriteAllBytes(_vault.GetVaultPath($"ada_vault_{hiddenId}.vlt"), ciphertext);
                    }
                    finally {
                        Array.Clear(wordsBytes);
                    }
                }
                finally {
                    Array.Clear(chars);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "unlock")
            {
                var bytes = twrite.Data.Span;
                if (bytes.Length == 0) throw new NinePProtocolException("Password is required to unlock wallet.");

                using var password = new SecureString();
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    foreach (char c in chars) if (c != '\n' && c != '\r') password.AppendChar(c);
                }
                finally {
                    Array.Clear(chars);
                }
                password.MakeReadOnly();

                byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"ada_vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var wordsBytes = _vault.DecryptToBytes(encrypted, password);
                    if (wordsBytes != null)
                    {
                        try {
                            if (_unlockedMnemonic != null) Array.Clear(_unlockedMnemonic);
                            _unlockedMnemonic = GC.AllocateArray<byte>(wordsBytes.Length, pinned: true);
                            wordsBytes.CopyTo(_unlockedMnemonic, 0);
                            _unlockedAddress = "Cardano Wallet Unlocked"; 
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        finally {
                            Array.Clear(wordsBytes);
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "cardano";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "import" || name == "create" || name == "unlock") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new CardanoFileSystem(_config, _vault);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedMnemonic != null)
        {
            clone._unlockedMnemonic = GC.AllocateArray<byte>(_unlockedMnemonic.Length, pinned: true);
            _unlockedMnemonic.CopyTo(clone._unlockedMnemonic, 0);
        }
        clone._unlockedAddress = _unlockedAddress;
        return clone;
    }
}

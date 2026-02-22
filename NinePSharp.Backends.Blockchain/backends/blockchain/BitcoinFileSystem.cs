using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class BitcoinFileSystem : INinePFileSystem
{
    private readonly BitcoinBackendConfig _config;
    private readonly RPCClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedPrivateKey;
    private string? _unlockedAddress;

    public bool DotU { get; set; }

    public BitcoinFileSystem(BitcoinBackendConfig config, RPCClient? rpcClient, ILuxVaultService vault)
    {
        _config = config;
        _rpcClient = rpcClient;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0] == "wallets" && path.Count == 1) return true;
        if (path[0] == "transactions" && path.Count == 1) return true;
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
                files.Add(("balance", QidType.QTFILE));
                files.Add(("address", QidType.QTFILE));
                files.Add(("send", QidType.QTFILE));
                files.Add(("transactions", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "wallets")
            {
                files.Add(("create", QidType.QTFILE));
                files.Add(("import", QidType.QTFILE));
                files.Add(("unlock", QidType.QTFILE));
            }
            else if (_currentPath[0] == "transactions")
            {
                // List dynamic transactions if available
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                if (f.Name == "create" || f.Name == "import" || f.Name == "unlock" || f.Name == "send") mode = 0666;
                
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
            switch (_currentPath.Last().ToLowerInvariant())
            {
                case "unlock":
                    result = _unlockedAddress != null ? $"Unlocked: {_unlockedAddress}\n" : "Locked\n";
                    break;
                case "balance":
                    if (_rpcClient != null) {
                        try {
                            var bal = await _rpcClient.GetBalanceAsync();
                            result = bal.ToString() + " BTC\n";
                        } catch (Exception ex) { result = $"Error: {ex.Message}\n"; }
                    }
                    else result = "0.00000000 BTC (Offline)\n";
                    break;
                case "address":
                    result = _unlockedAddress ?? "No wallet unlocked.\n";
                    break;
                case "status":
                    result = $"Network: {GetNetwork()}\nRPC: {_config.RpcUrl ?? "N/A"}\n";
                    break;
                case "transactions":
                    result = "No recent transactions.\n";
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

                var key = new Key(); // Bitcoin Private Key
                byte[] pkBytes = key.ToBytes();
                
                try {
                    var ciphertext = _vault.Encrypt(pkBytes, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"btc_vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(pkBytes);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:wif
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim();
                    var parts = fullStr.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:wif'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var wif = parts[1];
                    try {
                        var key = Key.Parse(wif, GetNetwork());
                        byte[] pkBytes = key.ToBytes();
                        try {
                            var ciphertext = _vault.Encrypt(pkBytes, password);
                            byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                            var seed = _vault.DeriveSeed(password, idSalt);
                            var hiddenId = _vault.GenerateHiddenId(seed);
                            
                            File.WriteAllBytes(_vault.GetVaultPath($"btc_vault_{hiddenId}.vlt"), ciphertext);
                        }
                        finally {
                            Array.Clear(pkBytes);
                        }
                    } catch { throw new NinePProtocolException("Invalid WIF."); }
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

                byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"btc_vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var pkBytes = _vault.DecryptToBytes(encrypted, password);
                    if (pkBytes != null)
                    {
                        try {
                            if (_unlockedPrivateKey != null) Array.Clear(_unlockedPrivateKey);
                            _unlockedPrivateKey = GC.AllocateArray<byte>(pkBytes.Length, pinned: true);
                            pkBytes.CopyTo(_unlockedPrivateKey, 0);
                            
                            var key = new Key(pkBytes);
                            _unlockedAddress = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, GetNetwork()).ToString();
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        finally {
                            Array.Clear(pkBytes);
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    private Network GetNetwork() => _config.Network.ToLower() switch
    {
        "testnet" => Network.TestNet,
        "regtest" => Network.RegTest,
        _ => Network.Main
    };

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "bitcoin";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "send" || name == "import" || name == "create" || name == "unlock") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new BitcoinFileSystem(_config, _rpcClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedPrivateKey != null)
        {
            clone._unlockedPrivateKey = GC.AllocateArray<byte>(_unlockedPrivateKey.Length, pinned: true);
            _unlockedPrivateKey.CopyTo(clone._unlockedPrivateKey, 0);
        }
        clone._unlockedAddress = _unlockedAddress;
        return clone;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Solnet.Rpc;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class SolanaFileSystem : INinePFileSystem
{
    private readonly SolanaBackendConfig _config;
    private readonly IRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private ProtectedSecret? _protectedPrivateKey;
    private string? _unlockedAddress;

    public SolanaFileSystem(SolanaBackendConfig config, IRpcClient? rpcClient, ILuxVaultService vault)
    {
        _config = config;
        _rpcClient = rpcClient;
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
            if (!IsDirectory(tempPath))
            {
                if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                break;
            }

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

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, GetQid(_currentPath), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        string result = "";
        if (_currentPath.Count == 0)
        {
            result = "wallets/\nbalance\nstatus\n";
        }
        else if (_currentPath[0] == "wallets")
        {
            if (_currentPath.Count == 1) result = "create\nimport\nunlock\n";
            else if (_currentPath.Count == 2 && _currentPath[1] == "unlock") result = _unlockedAddress != null ? $"Unlocked: {_unlockedAddress}\n" : "Locked\n";
        }
        else if (_currentPath.Count == 1)
        {
            switch (_currentPath[0])
            {
                case "balance":
                    result = "0.0 SOL (Mock)\n";
                    break;
                case "status":
                    result = $"RPC: {_config.RpcUrl}\nAddress: {_unlockedAddress ?? "None"}\n";
                    break;
            }
        }

        byte[] allData = Encoding.UTF8.GetBytes(result);
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
                using var password = new SecureString();
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                foreach (char c in input) password.AppendChar(c);
                password.MakeReadOnly();

                var wallet = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
                var account = wallet.GetAccount(0);
                
                var ciphertext = _vault.Encrypt(account.PrivateKey.Key, password);
                byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                
                File.WriteAllBytes($"sol_vault_{hiddenId}.vlt", ciphertext);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:base58PrivKey
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                var parts = input.Split(':', 2);
                if (parts.Length != 2) throw new NinePProtocolException("Invalid format. Use 'password:base58PrivKey'");

                using var password = new SecureString();
                foreach (char c in parts[0]) password.AppendChar(c);
                password.MakeReadOnly();

                var privKeyBase58 = parts[1];
                try { 
                    var account = new Account(privKeyBase58, ""); // Solnet.Wallet.Account(privateKey, publicKey)
                    var ciphertext = _vault.Encrypt(account.PrivateKey.Key, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes($"sol_vault_{hiddenId}.vlt", ciphertext);
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                } catch { throw new NinePProtocolException("Invalid Solana private key."); }
            }
            else if (_currentPath[1] == "unlock")
            {
                using var password = new SecureString();
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                foreach (char c in input) password.AppendChar(c);
                password.MakeReadOnly();

                byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = $"sol_vault_{hiddenId}.vlt";

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var privKey = _vault.DecryptToBytes(encrypted, password);
                    if (privKey != null)
                    {
                        _protectedPrivateKey?.Dispose();
                        _protectedPrivateKey = new ProtectedSecret(Convert.ToHexString(privKey));
                        
                        var account = new Account(privKey, Array.Empty<byte>());
                        _unlockedAddress = account.PublicKey;
                        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                    }
                }
            }
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "solana";
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
        var clone = new SolanaFileSystem(_config, _rpcClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._protectedPrivateKey = _protectedPrivateKey;
        clone._unlockedAddress = _unlockedAddress;
        return clone;
    }
}

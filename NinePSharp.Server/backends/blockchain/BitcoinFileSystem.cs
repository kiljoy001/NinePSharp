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
using NinePSharp.Server.Configuration;
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
    
    private ProtectedSecret? _protectedPrivateKey;
    private string? _unlockedAddress;

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
            result = "wallets/\nbalance\naddress\nsend\ntransactions/\nstatus\n";
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
                    result = $"Network: {_config.Network}\nRPC: {_config.RpcUrl ?? "N/A"}\n";
                    break;
                case "transactions":
                    result = "No recent transactions.\n";
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
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                if (string.IsNullOrWhiteSpace(input)) throw new NinePProtocolException("Password is required for wallet creation.");

                using var password = new SecureString();
                foreach (char c in input) password.AppendChar(c);
                password.MakeReadOnly();

                var key = new Key(); // Bitcoin Private Key
                var secret = key.GetWif(GetNetwork());
                
                var ciphertext = _vault.Encrypt(secret.ToString(), password);
                byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                
                File.WriteAllBytes($"btc_vault_{hiddenId}.vlt", ciphertext);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:wif
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                var parts = input.Split(':', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                    throw new NinePProtocolException("Invalid format or missing password. Use 'password:wif'");

                using var password = new SecureString();
                foreach (char c in parts[0]) password.AppendChar(c);
                password.MakeReadOnly();

                var wif = parts[1];
                try { Key.Parse(wif, GetNetwork()); } catch { throw new NinePProtocolException("Invalid WIF."); }

                var ciphertext = _vault.Encrypt(wif, password);
                byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                
                File.WriteAllBytes($"btc_vault_{hiddenId}.vlt", ciphertext);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "unlock")
            {
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                if (string.IsNullOrWhiteSpace(input)) throw new NinePProtocolException("Password is required to unlock wallet.");

                using var password = new SecureString();
                foreach (char c in input) password.AppendChar(c);
                password.MakeReadOnly();

                byte[] idSalt = Encoding.UTF8.GetBytes("Bitcoin_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = $"btc_vault_{hiddenId}.vlt";

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var wif = _vault.Decrypt(encrypted, password);
                    if (wif != null)
                    {
                        _protectedPrivateKey?.Dispose();
                        _protectedPrivateKey = new ProtectedSecret(wif);
                        
                        var key = Key.Parse(wif, GetNetwork());
                        _unlockedAddress = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, GetNetwork()).ToString();
                        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
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
        clone._protectedPrivateKey = _protectedPrivateKey;
        clone._unlockedAddress = _unlockedAddress;
        return clone;
    }
}

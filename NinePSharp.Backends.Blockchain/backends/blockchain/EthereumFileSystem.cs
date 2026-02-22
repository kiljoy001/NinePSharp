using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class EthereumFileSystem : INinePFileSystem
{
    private readonly EthereumBackendConfig _config;
    private IWeb3 _web3;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedPrivateKey;
    private string? _unlockedAccount;
    
    public bool DotU { get; set; }
    
    private Dictionary<string, string> _trackedTxs = new(); // txHash -> status

    private const string ERC20_ABI = @"[{'constant':false,'inputs':[{'name':'_to','type':'address'},{'name':'_value','type':'uint256'}],'name':'transfer','outputs':[{'name':'success','type':'bool'}],'payable':false,'stateMutability':'nonpayable','type':'function'}]";

    public EthereumFileSystem(EthereumBackendConfig config, IWeb3 web3, ILuxVaultService vault)
    {
        _config = config;
        _web3 = web3;
        _vault = vault;
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

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0] == "wallets" && path.Count == 1) return true;
        if (path[0] == "contracts" && (path.Count == 1 || path.Count == 2)) return true;
        return false;
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, new Qid(IsDirectory(_currentPath) ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0);

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
                files.Add(("contracts", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "wallets")
            {
                files.Add(("create", QidType.QTFILE));
                files.Add(("import", QidType.QTFILE));
                files.Add(("unlock", QidType.QTFILE));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "contracts")
            {
                // List dynamic contracts or specific nodes if at Count == 1 or 2
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
            if (last == "unlock")
            {
                result = _unlockedAccount != null ? $"Unlocked: {_unlockedAccount}\n" : "Locked\n";
            }
            else if (last == "status")
            {
                if (_currentPath.Count > 0 && _currentPath[0] == "wallets")
                {
                    result = _unlockedAccount != null ? $"Unlocked: {_unlockedAccount}\n" : "Locked\n";
                }
                else
                {
                    try
                    {
                        var bal = await _web3.Eth.GetBalance.SendRequestAsync(_config.DefaultAccount);
                        result = $"Connected to: {_config.RpcUrl}\nDefault Account: {_config.DefaultAccount}\nBalance: {Web3.Convert.FromWei(bal.Value)} ETH\n";
                    }
                    catch (Exception ex)
                    {
                        result = $"Connected to: {_config.RpcUrl}\nDefault Account: {_config.DefaultAccount}\nBalance: unavailable ({ex.Message})\n";
                    }

                    if (_unlockedAccount != null)
                    {
                        result += $"Unlocked: {_unlockedAccount}\n";
                    }

                    if (_trackedTxs.Any())
                    {
                        result += "\nTracked Transactions:\n";
                        foreach (var tx in _trackedTxs)
                        {
                            result += $"{tx.Key}: {tx.Value}\n";
                        }
                    }
                }
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

                var ecKey = Nethereum.Signer.EthECKey.GenerateKey();
                var pk = ecKey.GetPrivateKeyAsBytes(); // Get as bytes
                
                try {
                    var ciphertext = _vault.Encrypt(pk, password);
                    
                    byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(pk);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:privateKey
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim(); // Temporary leakage of full import string.
                    // This could be improved, but let's at least clear the chars.
                    var parts = fullStr.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:privateKey'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var pkStr = parts[1];
                    // Convert pkStr to bytes and use it
                    byte[] pkBytes = Convert.FromHexString(pkStr);
                    try {
                        var ciphertext = _vault.Encrypt(pkBytes, password);
                        byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                        var seed = _vault.DeriveSeed(password, idSalt);
                        var hiddenId = _vault.GenerateHiddenId(seed);
                        
                        File.WriteAllBytes(_vault.GetVaultPath($"vault_{hiddenId}.vlt"), ciphertext);
                    }
                    finally {
                        Array.Clear(pkBytes);
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

                byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var pk = _vault.DecryptToBytes(encrypted, password);
                    if (pk != null)
                    {
                        try {
                            if (_unlockedPrivateKey != null) Array.Clear(_unlockedPrivateKey);
                            _unlockedPrivateKey = GC.AllocateArray<byte>(pk.Length, pinned: true);
                            pk.CopyTo(_unlockedPrivateKey, 0);
                            
                            var account = new Account(Convert.ToHexString(pk));
                            _unlockedAccount = account.Address;
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        finally {
                            Array.Clear(pk);
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }

        if (_currentPath.Count >= 4 && _currentPath[0] == "contracts")
        {
            if (_unlockedPrivateKey == null)
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            var contractAddr = _currentPath[1];
            var callInfo = AbiParser.ParseCall(_currentPath[3]);
            if (callInfo != null)
            {
                string pkHex = Convert.ToHexString(_unlockedPrivateKey);
                var account = new Account(pkHex);
                var web3WithAccount = new Web3(account, _config.RpcUrl);
                
                if (callInfo.Value.Name == "transfer" && callInfo.Value.Arguments.Length == 2)
                {
                    var to = callInfo.Value.Arguments[0];
                    var amount = System.Numerics.BigInteger.Parse(callInfo.Value.Arguments[1]);
                    
                    var contract = web3WithAccount.Eth.GetContract(ERC20_ABI.Replace('\'', '\"'), contractAddr);
                    var function = contract.GetFunction("transfer");
                    
                    var txHash = await function.SendTransactionAsync(account.Address, to, amount);
                    _trackedTxs[txHash] = "Pending";
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "eth";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0644 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new EthereumFileSystem(_config, _web3, _vault);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedPrivateKey != null)
        {
            clone._unlockedPrivateKey = GC.AllocateArray<byte>(_unlockedPrivateKey.Length, pinned: true);
            _unlockedPrivateKey.CopyTo(clone._unlockedPrivateKey, 0);
        }
        clone._unlockedAccount = _unlockedAccount;
        clone._trackedTxs = _trackedTxs;
        return clone;
    }
}

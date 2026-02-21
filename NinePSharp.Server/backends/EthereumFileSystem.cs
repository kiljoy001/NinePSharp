using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nethereum.Web3;
using NinePSharp.Messages;
using NinePSharp.Protocol;
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
    private string? _unlockedAccount;
    private string? _privateKey;
    private Dictionary<string, string> _trackedTxs = new(); // txHash -> status

    public EthereumFileSystem(EthereumBackendConfig config, IWeb3 web3, ILuxVaultService vault)
    {
        _config = config;
        _web3 = web3;
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
        if (path.Count == 0) return true;
        if (path[0] == "contracts")
        {
            if (path.Count == 1) return true; // /contracts
            if (path.Count == 2) return true; // /contracts/<addr>
            if (path.Count == 3 && (path[2] == "call" || path[2] == "transact")) return true;
        }
        if (path[0] == "wallets" && path.Count == 1) return true;
        if (path[0] == "tx" && (path.Count == 1 || path.Count == 2)) return true;
        return false;
    }

    private Qid GetQid()
    {
        var type = IsDirectory(_currentPath) ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", _currentPath);
        ulong hash = (ulong)pathStr.GetHashCode();
        
        // Encode Wave 6 for wallets/unlock to signal it's handled via Invisible Lock
        if (_currentPath.Count > 0 && _currentPath[0] == "wallets")
        {
            hash = HolographicUtils.EncodeWave(hash, HolographicUtils.WAVE_6_INVISIBLE_LOCK);
        }

        return new Qid(type, 0, hash);
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        return new Ropen(topen.Tag, GetQid(), 0);
    }

    private const string ERC20_ABI = @"[
        {'constant':true,'inputs':[],'name':'name','outputs':[{'name':'','type':'string'}],'payable':false,'stateMutability':'view','type':'function'},
        {'constant':true,'inputs':[],'name':'symbol','outputs':[{'name':'','type':'string'}],'payable':false,'stateMutability':'view','type':'function'},
        {'constant':true,'inputs':[],'name':'totalSupply','outputs':[{'name':'','type':'uint256'}],'payable':false,'stateMutability':'view','type':'function'}
    ]";

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset > 0) return new Rread(tread.Tag, Array.Empty<byte>());

        string result = "";

        if (_currentPath.Count == 0)
        {
            result = "balance\ncontracts\nwallets\n";
        }
        else if (_currentPath[0] == "balance")
        {
            var bal = await _web3.Eth.GetBalance.SendRequestAsync(_config.DefaultAccount);
            result = $"{Web3.Convert.FromWei(bal.Value)} ETH\n";
        }
        else if (_currentPath[0] == "wallets")
        {
            if (_currentPath.Count == 1)
            {
                result = "create\nunlock\nstatus\n";
            }
            else if (_currentPath[1] == "status")
            {
                result = _unlockedAccount != null 
                    ? $"Unlocked: {_unlockedAccount}\n" 
                    : "No wallet unlocked for this session.\n";
            }
        }
        else if (_currentPath[0] == "tx")
        {
            if (_currentPath.Count == 1)
            {
                result = string.Join("\n", _trackedTxs.Keys) + "\n";
            }
            else if (_currentPath.Count == 2)
            {
                var txHash = _currentPath[1];
                if (_trackedTxs.TryGetValue(txHash, out var status))
                {
                    result = $"Status: {status}\n";
                    // Attempt to update status if it's pending
                    if (status == "Pending")
                    {
                        var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
                        if (receipt != null)
                        {
                            _trackedTxs[txHash] = receipt.Status.Value == 1 ? "Success" : "Failed";
                            result = $"Status: {_trackedTxs[txHash]}\n";
                        }
                    }
                }
                else
                {
                    result = "Transaction not tracked or invalid.\n";
                }
            }
        }
        else if (_currentPath[0] == "contracts")
        {
            if (_currentPath.Count == 1)
            {
                result = "0x...\n"; // Placeholder for tracked contracts
            }
            else if (_currentPath.Count == 3)
            {
                if (_currentPath[2] == "call") result = "name()\nsymbol()\ntotalSupply()\n";
                else if (_currentPath[2] == "transact") result = "transfer(to,amount)\n";
            }
            else if (_currentPath.Count == 4 && _currentPath[2] == "call")
            {
                var contractAddr = _currentPath[1];
                var callInfo = AbiParser.ParseCall(_currentPath[3]);
                if (callInfo != null)
                {
                    try
                    {
                        var contract = _web3.Eth.GetContract(ERC20_ABI.Replace('\'', '\"'), contractAddr);
                        var function = contract.GetFunction(callInfo.Value.Name);
                        
                        if (callInfo.Value.Name == "totalSupply")
                        {
                            result = (await function.CallAsync<System.Numerics.BigInteger>()).ToString() + "\n";
                        }
                        else 
                        {
                            result = await function.CallAsync<string>() + "\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        result = $"Error: {ex.Message}\n";
                    }
                }
            }
        }

        return new Rread(tread.Tag, System.Text.Encoding.UTF8.GetBytes(result));
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        var input = System.Text.Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
        
        if (_currentPath.Count == 2 && _currentPath[0] == "wallets")
        {
            if (_currentPath[1] == "create")
            {
                var password = input;
                var ecKey = Nethereum.Signer.EthECKey.GenerateKey();
                var pk = ecKey.GetPrivateKey();
                
                // 1. Encrypt with Invisible Lock (Elligator)
                var ciphertext = _vault.EncryptInvisible(Encoding.UTF8.GetBytes(pk), password);
                
                // 2. Derive Hidden ID (Secret Pointer)
                byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                
                // 3. Save to hidden file
                File.WriteAllBytes($"vault_{hiddenId}.vlt", ciphertext);
                
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "unlock")
            {
                var password = input;
                
                // 1. Derive the Hidden ID from password
                byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = $"vault_{hiddenId}.vlt";

                if (File.Exists(vaultFile))
                {
                    try
                    {
                        var payload = File.ReadAllBytes(vaultFile);
                        var recoveredPkBytes = _vault.DecryptInvisible(payload, password);
                        
                        if (recoveredPkBytes != null)
                        {
                            _privateKey = Encoding.UTF8.GetString(recoveredPkBytes);
                            var account = new Nethereum.Web3.Accounts.Account(_privateKey);
                            _unlockedAccount = account.Address;
                            _web3 = new Nethereum.Web3.Web3(account, _config.RpcUrl);
                        }
                    }
                    catch { /* Decryption failed */ }
                }

                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }
        else if (_currentPath.Count == 4 && _currentPath[0] == "contracts" && _currentPath[2] == "transact")
        {
            if (_unlockedAccount == null || _privateKey == null)
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            var contractAddr = _currentPath[1];
            var callInfo = AbiParser.ParseCall(_currentPath[3]);
            if (callInfo != null)
            {
                var account = new Nethereum.Web3.Accounts.Account(_privateKey);
                var web3WithAccount = new Web3(account, _config.RpcUrl);
                
                if (callInfo.Value.Name == "transfer" && callInfo.Value.Arguments.Length == 2)
                {
                    var to = callInfo.Value.Arguments[0];
                    var amount = System.Numerics.BigInteger.Parse(callInfo.Value.Arguments[1]);
                    
                    var contract = web3WithAccount.Eth.GetContract(ERC20_ABI.Replace('\'', '\"'), contractAddr);
                    var function = contract.GetFunction("transfer");
                    var txHash = await function.SendTransactionAsync(account.Address, to, amount);
                    _trackedTxs[txHash] = "Pending";
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
            }
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "eth";
        var isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new EthereumFileSystem(_config, _web3, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._unlockedAccount = _unlockedAccount;
        clone._privateKey = _privateKey;
        return clone;
    }
}

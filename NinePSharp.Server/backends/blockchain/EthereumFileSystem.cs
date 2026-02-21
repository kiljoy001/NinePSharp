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
using NinePSharp.Server.Configuration;
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
    
    // Memory-protected private key. Decrypted only momentarily for signing.
    private ProtectedSecret? _protectedPrivateKey;
    private string? _unlockedAccount;
    
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
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        string result = "";
        if (_currentPath.Count == 0)
        {
            result = "wallets/\ncontracts/\nstatus\n";
        }
        else if (_currentPath[0] == "wallets")
        {
            if (_currentPath.Count == 1) result = "create\nunlock\n";
            else if (_currentPath.Count == 2 && _currentPath[1] == "unlock") result = _unlockedAccount != null ? $"Unlocked: {_unlockedAccount}\n" : "Locked\n";
        }
        else if (_currentPath[0] == "status")
        {
            var bal = await _web3.Eth.GetBalance.SendRequestAsync(_config.DefaultAccount);
            result = $"Connected to: {_config.RpcUrl}\nDefault Account: {_config.DefaultAccount}\nBalance: {Web3.Convert.FromWei(bal.Value)} ETH\n";
            if (_trackedTxs.Any())
            {
                result += "\nTracked Transactions:\n";
                foreach (var tx in _trackedTxs)
                {
                    result += $"{tx.Key}: {tx.Value}\n";
                }
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

                var ecKey = Nethereum.Signer.EthECKey.GenerateKey();
                var pk = ecKey.GetPrivateKey();
                
                var ciphertext = _vault.Encrypt(pk, password);
                
                byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                
                File.WriteAllBytes($"vault_{hiddenId}.vlt", ciphertext);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "unlock")
            {
                using var password = new SecureString();
                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                foreach (char c in input) password.AppendChar(c);
                password.MakeReadOnly();

                byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = $"vault_{hiddenId}.vlt";

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var pk = _vault.Decrypt(encrypted, password);
                    if (pk != null)
                    {
                        _protectedPrivateKey?.Dispose();
                        _protectedPrivateKey = new ProtectedSecret(pk);
                        
                        var account = new Account(pk);
                        _unlockedAccount = account.Address;
                        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                    }
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        if (_currentPath.Count >= 4 && _currentPath[0] == "contracts")
        {
            if (_protectedPrivateKey == null)
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            var contractAddr = _currentPath[1];
            var callInfo = AbiParser.ParseCall(_currentPath[3]);
            if (callInfo != null)
            {
                await _protectedPrivateKey.UseAsync(async pkMemory => {
                    var account = new Account(Convert.ToHexString(pkMemory.Span));
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
                });
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "eth";
        var isDir = name != "status" && !name.EndsWith(".vlt"); // Simple heuristic
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0644 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new EthereumFileSystem(_config, _web3, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._protectedPrivateKey = _protectedPrivateKey; 
        clone._unlockedAccount = _unlockedAccount;
        clone._trackedTxs = _trackedTxs;
        return clone;
    }
}

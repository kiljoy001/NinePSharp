using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System.Collections.Generic;
using System.Linq;

namespace NinePSharp.Server.Backends;

/// <summary>
/// Provides a 9P filesystem interface to the Ethereum blockchain.
/// Allows querying balances, reading contract ABIs, and executing contract calls via virtual files.
/// </summary>
public class EthereumFileSystem : INinePFileSystem
{
    private readonly EthereumBackendConfig _config;
    private readonly JsonRpcClient _rpcClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;
    private readonly X509Certificate2? _certificate;
    private List<string> _currentPath = new();
    
    private string? _proxyAccount; 
    
    public bool DotU { get; set; }
    
    private Dictionary<string, string> _trackedTxs = new(); // txHash -> status
    private Dictionary<string, string> _contractAbis = new(); // address -> abi

    public EthereumFileSystem(EthereumBackendConfig config, JsonRpcClient rpcClient, ILuxVaultService vault, IEmercoinAuthService? authService = null, X509Certificate2? certificate = null)
    {
        _config = config;
        _rpcClient = rpcClient;
        _vault = vault;
        _authService = authService;
        _certificate = certificate;
    }

    private async Task EnsureAuthorizedAsync()
    {
        if (_authService == null) return; // Emercoin security is optional

        if (_certificate == null)
        {
            throw new NinePProtocolException("Connection must be secured with a client certificate for blockchain operations.");
        }

        bool authorized = await _authService.IsCertificateAuthorizedAsync(_certificate);
        if (!authorized)
        {
            throw new NinePProtocolException("Certificate is not authorized in Emercoin NVS.");
        }
    }

    private void EnsureMethodAllowed(string method)
    {
        if (_config.AllowedMethods == null || _config.AllowedMethods.Count == 0) return; // Allow all if not specified (backward compatibility)
        if (!_config.AllowedMethods.Contains(method))
        {
            throw new NinePProtocolException($"RPC method '{method}' is not allowed by the server configuration.");
        }
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
                if (tempPath.Count == 1 && tempPath[0] == "contracts") {
                    // Allow any name as a contract address
                } else if (tempPath.Count == 2 && tempPath[0] == "contracts") {
                    if (name != "abi" && name != "call") {
                         if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                         break;
                    }
                }
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
        if (path[0] == "wallets") {
             if (path.Count == 1) return true; // /wallets
        }
        if (path[0] == "contracts") {
             if (path.Count == 1) return true; // /contracts
             if (path.Count == 2) return true; // /contracts/<address>
        }
        return false;
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, new Qid(IsDirectory(_currentPath) ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        await EnsureAuthorizedAsync();
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
                files.Add(("use", QidType.QTFILE));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "contracts")
            {
                if (_currentPath.Count == 1) {
                    foreach (var addr in _contractAbis.Keys) files.Add((addr, QidType.QTDIR));
                } else if (_currentPath.Count == 2) {
                    files.Add(("abi", QidType.QTFILE));
                    files.Add(("call", QidType.QTFILE));
                }
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                if (f.Name == "use" || f.Name == "abi" || f.Name == "call") mode = 0666;
                
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
            var last = _currentPath.Last();
            string result = "";

            if (last == "use")            {
                result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
            }
            else if (last == "abi" && _currentPath.Count == 3) {
                var addr = _currentPath[1];
                result = (_contractAbis.TryGetValue(addr, out var abi) ? abi : "DEFAULT_ERC20_ABI") + "\n";
            }
            else if (last == "status")
            {
                if (_currentPath.Count > 0 && _currentPath[0] == "wallets")
                {
                    result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
                }
                else
                {
                    try
                    {
                        EnsureMethodAllowed("eth_getBalance");
                        var balanceResult = await _rpcClient.CallAsync("eth_getBalance", new object[] { _config.DefaultAccount, "latest" });
                        result = $"Connected to: {_config.RpcUrl}\nDefault Account: {_config.DefaultAccount}\nBalance: {balanceResult} (Wei)\n";
                    }
                    catch (Exception ex)
                    {
                        result = $"Connected to: {_config.RpcUrl}\nDefault Account: {_config.DefaultAccount}\nBalance: unavailable ({ex.Message})\n";
                    }

                    if (_proxyAccount != null)
                    {
                        result += $"Active Proxy Account: {_proxyAccount}\n";
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
        await EnsureAuthorizedAsync();
        if (_currentPath.Count == 2 && _currentPath[0] == "wallets")
        {
            if (_currentPath[1] == "use")
            {
                var bytes = twrite.Data.Span;
                if (bytes.Length == 0) throw new NinePProtocolException("Account address is required.");

                var addr = Encoding.UTF8.GetString(bytes).Trim();
                if (addr.StartsWith("0x") && addr.Length == 42)
                {
                    _proxyAccount = addr;
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
                throw new NinePProtocolException("Invalid Ethereum address format.");
            }
        }

        if (_currentPath.Count == 3 && _currentPath[0] == "contracts")
        {
            var addr = _currentPath[1];
            if (_currentPath[2] == "abi") {
                _contractAbis[addr] = Encoding.UTF8.GetString(twrite.Data.Span).Trim();
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            
            if (_currentPath[2] == "call") {
                if (_proxyAccount == null)
                {
                    throw new InvalidOperationException("No account selected. Write address to /wallets/use first.");
                }

                EnsureMethodAllowed("eth_sendTransaction");
                var contractAddr = _currentPath[1];
                var data = Encoding.UTF8.GetString(twrite.Data.Span).Trim();
                
                // Simple eth_sendTransaction proxy
                var txParams = new JsonObject
                {
                    ["from"] = _proxyAccount,
                    ["to"] = contractAddr,
                    ["data"] = data.StartsWith("0x") ? data : "0x" + data
                };

                var txHashNode = await _rpcClient.CallAsync("eth_sendTransaction", new object[] { txParams });
                var txHash = txHashNode?.ToString() ?? "unknown";
                _trackedTxs[txHash] = "Pending";
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

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePPermissionDeniedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePPermissionDeniedException();

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var name = _currentPath.LastOrDefault() ?? "eth";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu : 0644;
        if (name == "use" || name == "abi" || name == "call") mode = 0666;

        var qid = new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, DeterministicHash.GetStableHash64(string.Join("/", _currentPath)));
        return new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode);
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePPermissionDeniedException();

    public INinePFileSystem Clone()
    {
        var clone = new EthereumFileSystem(_config, _rpcClient, _vault, _authService, _certificate);
        clone._currentPath = new List<string>(_currentPath);
        clone._proxyAccount = _proxyAccount;
        clone._trackedTxs = _trackedTxs;
        return clone;
    }
}

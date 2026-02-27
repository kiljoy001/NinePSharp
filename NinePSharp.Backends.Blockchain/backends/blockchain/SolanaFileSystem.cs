using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class SolanaFileSystem : INinePFileSystem
{
    private readonly SolanaBackendConfig _config;
    private readonly JsonRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;
    private readonly X509Certificate2? _certificate;
    private List<string> _currentPath = new();
    
    private string? _proxyAccount; 
    private decimal _mockBalanceSol = 25.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public SolanaFileSystem(SolanaBackendConfig config, JsonRpcClient? rpcClient, ILuxVaultService vault, IEmercoinAuthService? authService = null, X509Certificate2? certificate = null)
    {
        _config = config;
        _rpcClient = rpcClient;
        _vault = vault;
        _authService = authService;
        _certificate = certificate;
    }

    private async Task EnsureAuthorizedAsync()
    {
        if (_authService == null) return;
        if (_certificate == null) throw new NinePProtocolException("Connection must be secured with a client certificate.");
        if (!await _authService.IsCertificateAuthorizedAsync(_certificate))
            throw new NinePProtocolException("Certificate is not authorized in Emercoin NVS.");
    }

    private void EnsureMethodAllowed(string method)
    {
        if (_config.AllowedMethods == null || _config.AllowedMethods.Count == 0) return;
        if (!_config.AllowedMethods.Contains(method))
            throw new NinePProtocolException($"RPC method '{method}' is not allowed.");
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0] == "wallets") return path.Count == 1;
        return false;
    }

    private Qid GetQid(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", path);
        return new Qid(type, 0, DeterministicHash.GetStableHash64(pathStr));
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
            qids.Add(new Qid(IsDirectory(tempPath) ? QidType.QTDIR : QidType.QTFILE, 0, DeterministicHash.GetStableHash64(string.Join("/", tempPath))));
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
        await EnsureAuthorizedAsync();
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
                files.Add(("transactions", QidType.QTFILE));
                files.Add(("status", QidType.QTFILE));
                files.Add(("network", QidType.QTFILE));
            }
            else if (_currentPath[0] == "wallets")
            {
                files.Add(("use", QidType.QTFILE));
                files.Add(("status", QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                if (f.Name == "use" || f.Name == "send") mode = 0666;
                
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
            if (last == "use")
            {
                result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
            }
            else if (last == "balance")
            {
                EnsureMethodAllowed("getBalance");
                result = await TryGetLiveBalanceTextAsync()
                    ?? $"{_mockBalanceSol.ToString("0.000000", CultureInfo.InvariantCulture)} SOL (Mock)\n";
            }
            else if (last == "address")
            {
                result = _proxyAccount ?? "No proxy account selected.\n";
            }
            else if (last == "transactions")
            {
                result = _mockTransactions.Count == 0
                    ? "No recent transactions.\n"
                    : string.Join('\n', _mockTransactions) + '\n';
            }
            else if (last == "status")
            {
                result = $"RPC: {_config.RpcUrl}\nActiveAccount: {_proxyAccount ?? "None"}\nTrackedTx: {_mockTransactions.Count}\nMode: {(_rpcClient != null ? "Live" : "Mock")}\n";
            }
            else if (last == "network")
            {
                result = _config.RpcUrl?.Contains("mainnet") == true ? "Mainnet\n" : "Devnet/Testnet\n";
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
        var bytes = twrite.Data.Span;
        int maxChars = Encoding.UTF8.GetMaxCharCount(bytes.Length);
        char[] chars = System.Buffers.ArrayPool<char>.Shared.Rent(maxChars);
        try {
            int charsDecoded = Encoding.UTF8.GetChars(bytes, chars);
            var charSpan = chars.AsSpan(0, charsDecoded).Trim();

            if (_currentPath.Count == 2 && _currentPath[0] == "wallets")
            {
                if (_currentPath[1] == "use")
                {
                    if (charSpan.Length == 0) throw new NinePProtocolException("Account address required.");
                    _proxyAccount = charSpan.ToString();
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
            }

            if (_currentPath.Count == 1 && _currentPath[0] == "send")
            {
                if (_proxyAccount == null && _rpcClient != null)
                {
                    throw new InvalidOperationException("No proxy account selected. Write address to /wallets/use first.");
                }

                var (to, amount) = ParseTransferCommand(charSpan, "address:amount");
                if (_rpcClient != null)
                {
                    EnsureMethodAllowed("sendTransaction");
                    throw new NinePProtocolException("Node-side signing for Solana is not supported via standard RPC proxy. Keystore on node required.");
                }

                if (_mockBalanceSol < amount)
                {
                    throw new NinePProtocolException("Insufficient funds.");
                }

                _mockBalanceSol -= amount;
                _mockTxCounter++;
                _mockTransactions.Insert(
                    0,
                    $"sol-mock-{_mockTxCounter:D6} to={to} amount={amount.ToString("0.######", CultureInfo.InvariantCulture)} status=confirmed");
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }
        finally {
            System.Buffers.ArrayPool<char>.Shared.Return(chars, clearArray: true);
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    private static (string To, decimal Amount) ParseTransferCommand(ReadOnlySpan<char> command, string expectedFormat)
    {
        int colon = command.IndexOf(':');
        if (colon == -1)
        {
            throw new NinePProtocolException($"Invalid format. Use '{expectedFormat}'");
        }

        var toSpan = command.Slice(0, colon).Trim();
        var amountSpan = command.Slice(colon + 1).Trim();

        if (toSpan.Length == 0)
        {
            throw new NinePProtocolException($"Invalid format. Use '{expectedFormat}'");
        }

        if (!decimal.TryParse(amountSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            throw new NinePProtocolException($"Invalid format. Use '{expectedFormat}'");
        }

        return (toSpan.ToString(), amount);
    }

    private async Task<string?> TryGetLiveBalanceTextAsync()
    {
        if (_rpcClient == null || string.IsNullOrWhiteSpace(_proxyAccount))
        {
            return null;
        }

        try
        {
            var balanceNode = await _rpcClient.CallAsync("getBalance", new object[] { _proxyAccount });
            if (balanceNode == null) return null;
            
            // Solana getBalance returns { value: uint64 } in some versions or just uint64
            decimal lamports = 0;
            if (balanceNode is System.Text.Json.Nodes.JsonObject obj && obj.TryGetPropertyValue("value", out var val))
                lamports = decimal.Parse(val!.ToString());
            else
                lamports = decimal.Parse(balanceNode.ToString());

            decimal sol = lamports / 1_000_000_000m;
            return $"{sol.ToString("0.000000", CultureInfo.InvariantCulture)} SOL (Live)\n";
        }
        catch
        {
            return null;
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "solana";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED : 0644;
        if (name == "use" || name == "send") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePPermissionDeniedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePPermissionDeniedException();

    public INinePFileSystem Clone()
    {
        var clone = new SolanaFileSystem(_config, _rpcClient, _vault, _authService, _certificate);
        clone._currentPath = new List<string>(_currentPath);
        clone._proxyAccount = _proxyAccount;
        clone._mockBalanceSol = _mockBalanceSol;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        return clone;
    }
}

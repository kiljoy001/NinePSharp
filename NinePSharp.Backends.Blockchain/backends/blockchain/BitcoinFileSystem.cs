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

public class BitcoinFileSystem : INinePFileSystem
{
    private readonly BitcoinBackendConfig _config;
    private readonly JsonRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;
    private readonly X509Certificate2? _certificate;
    private List<string> _currentPath = new();
    
    private string? _proxyAccount; 
    private decimal _mockBalanceBtc = 1.00000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public bool DotU { get; set; }

    public BitcoinFileSystem(BitcoinBackendConfig config, JsonRpcClient? rpcClient, ILuxVaultService vault, IEmercoinAuthService? authService = null, X509Certificate2? certificate = null)
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
            switch (_currentPath.Last().ToLowerInvariant())
            {
                case "use":
                    result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
                    break;
                case "balance":
                    if (_rpcClient != null) {
                        try {
                            EnsureMethodAllowed("getbalance");
                            var bal = await _rpcClient.CallAsync("getbalance");
                            result = bal?.ToString() + " BTC (Live)\n";
                        } catch (Exception ex) { result = $"Error: {ex.Message}\n"; }
                    }
                    else result = $"{_mockBalanceBtc.ToString("0.00000000", CultureInfo.InvariantCulture)} BTC (Mock)\n";
                    break;
                case "address":
                    result = _proxyAccount ?? "No proxy account selected.\n";
                    break;
                case "status":
                    result = $"Network: {_config.Network}\nRPC: {_config.RpcUrl ?? "N/A"}\nActiveAccount: {_proxyAccount ?? "None"}\nTrackedTx: {_mockTransactions.Count}\nMode: {(_rpcClient != null ? "Live" : "Mock")}\n";
                    break;
                case "transactions":
                    result = _mockTransactions.Count == 0
                        ? "No recent transactions.\n"
                        : string.Join('\n', _mockTransactions) + '\n';
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
        await EnsureAuthorizedAsync();
        if (_currentPath.Count == 2 && _currentPath[0] == "wallets")
        {
            if (_currentPath[1] == "use")
            {
                var bytes = twrite.Data.Span;
                if (bytes.Length == 0) throw new NinePProtocolException("Account name/address required.");
                _proxyAccount = Encoding.UTF8.GetString(bytes).Trim();
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            if (_proxyAccount == null && _rpcClient != null)
            {
                throw new InvalidOperationException("No proxy account selected. Write name to /wallets/use first.");
            }

            var transfer = ParseTransferCommand(twrite.Data, "address:amount");
            if (_rpcClient != null)
            {
                EnsureMethodAllowed("sendtoaddress");
                // Bitcoin RPC sendtoaddress usually takes [address, amount]
                var txHashNode = await _rpcClient.CallAsync("sendtoaddress", new object[] { transfer.To, transfer.Amount });
                var txHash = txHashNode?.ToString() ?? "unknown";
                _mockTxCounter++;
                _mockTransactions.Insert(
                    0,
                    $"{txHash} to={transfer.To} amount={transfer.Amount.ToString("0.00000000", CultureInfo.InvariantCulture)} status=submitted");
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }

            if (_mockBalanceBtc < transfer.Amount)
            {
                throw new NinePProtocolException("Insufficient funds.");
            }

            _mockBalanceBtc -= transfer.Amount;
            _mockTxCounter++;
            _mockTransactions.Insert(
                0,
                $"btc-mock-{_mockTxCounter:D6} to={transfer.To} amount={transfer.Amount.ToString("0.00000000", CultureInfo.InvariantCulture)} status=confirmed");
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    private static (string To, decimal Amount) ParseTransferCommand(ReadOnlyMemory<byte> payload, string expectedFormat)
    {
        var command = Encoding.UTF8.GetString(payload.Span).Trim();
        var parts = command.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new NinePProtocolException($"Invalid format. Use '{expectedFormat}'");
        }

        if (!decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            throw new NinePProtocolException($"Invalid format. Use '{expectedFormat}'");
        }

        return (parts[0], amount);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "bitcoin";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "send" || name == "use") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePPermissionDeniedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePPermissionDeniedException();

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var name = _currentPath.LastOrDefault() ?? "bitcoin";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "send" || name == "use") mode = 0666;

        var qid = GetQid(_currentPath);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new NinePSharp.Messages.Rgetattr(
            tgetattr.Tag,
            (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC,
            qid,
            mode,
            1000, 1000, 1, 0, 0, 4096, 0, now, 0, now, 0, now, 0, 0, 0, 0, 0
        );
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePPermissionDeniedException();

    public INinePFileSystem Clone()
    {
        var clone = new BitcoinFileSystem(_config, _rpcClient, _vault, _authService, _certificate);
        clone._currentPath = new List<string>(_currentPath);
        clone._proxyAccount = _proxyAccount;
        clone._mockBalanceBtc = _mockBalanceBtc;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        clone.DotU = DotU;
        return clone;
    }
}

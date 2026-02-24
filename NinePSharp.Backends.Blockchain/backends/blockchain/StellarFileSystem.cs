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

public class StellarFileSystem : INinePFileSystem
{
    private readonly StellarBackendConfig _config;
    private readonly JsonRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;
    private readonly X509Certificate2? _certificate;
    private List<string> _currentPath = new();
    
    private string? _proxyAccount; 
    private decimal _mockBalanceXlm = 50.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public bool DotU { get; set; }

    public StellarFileSystem(StellarBackendConfig config, JsonRpcClient? rpcClient, ILuxVaultService vault, IEmercoinAuthService? authService = null, X509Certificate2? certificate = null)
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
            throw new NinePProtocolException($"Operation '{method}' is not allowed.");
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
            switch (last)
            {
                case "use":
                    result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
                    break;
                case "balance":
                    // Stellar usually uses REST (Horizon), not JSON-RPC.
                    // A pure proxy model would assume a specialized RPC gateway if using JSON-RPC.
                    result = $"{_mockBalanceXlm.ToString("0.000000", CultureInfo.InvariantCulture)} XLM (Mock)\n";
                    break;
                case "address":
                    result = _proxyAccount ?? "No proxy account selected.\n";
                    break;
                case "transactions":
                    result = _mockTransactions.Count == 0
                        ? "No recent transactions.\n"
                        : string.Join('\n', _mockTransactions) + '\n';
                    break;
                case "status":
                    result = $"Horizon: {_config.HorizonUrl}\nActiveAccount: {_proxyAccount ?? "None"}\nTrackedTx: {_mockTransactions.Count}\nMode: Mock\n";
                    break;
                case "network":
                    result = _config.UsePublicNetwork ? "Public\n" : "TestNet\n";
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
                if (bytes.Length == 0) throw new NinePProtocolException("Account ID required.");
                _proxyAccount = Encoding.UTF8.GetString(bytes).Trim();
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            throw new NinePProtocolException("Node-side signing for Stellar is not supported via standard Horizon proxy. Keystore on node or specialized signer required.");
        }
        
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "stellar";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED : 0644;
        if (name == "use" || name == "send") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePPermissionDeniedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePPermissionDeniedException();

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var name = _currentPath.LastOrDefault() ?? "stellar";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu : 0644;
        if (name == "use" || name == "send") mode = 0666;

        var qid = GetQid(_currentPath);
        return new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode);
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePPermissionDeniedException();

    public INinePFileSystem Clone()
    {
        var clone = new StellarFileSystem(_config, _rpcClient, _vault, _authService, _certificate);
        clone._currentPath = new List<string>(_currentPath);
        clone._proxyAccount = _proxyAccount;
        clone._mockBalanceXlm = _mockBalanceXlm;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        clone.DotU = DotU;
        return clone;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

public class CardanoFileSystem : INinePFileSystem
{
    private const decimal LovelacePerAda = 1_000_000m;
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly CardanoBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private readonly IEmercoinAuthService? _authService;
    private readonly X509Certificate2? _certificate;
    private readonly HttpClient _httpClient;
    private List<string> _currentPath = new();
    
    private string? _proxyAccount; 
    private decimal _mockBalanceAda = 100.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public CardanoFileSystem(CardanoBackendConfig config, ILuxVaultService vault, IEmercoinAuthService? authService = null, X509Certificate2? certificate = null, HttpClient? httpClient = null)
    {
        _config = config;
        _vault = vault;
        _authService = authService;
        _certificate = certificate;
        _httpClient = httpClient ?? SharedHttpClient;
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

    private bool HasLiveApi =>
        !string.IsNullOrWhiteSpace(_config.BlockfrostProjectId);

    private string ResolveBlockfrostApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_config.BlockfrostApiUrl))
        {
            return _config.BlockfrostApiUrl.TrimEnd('/');
        }

        return (_config.Network ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "main" or "mainnet" => "https://cardano-mainnet.blockfrost.io/api/v0",
            "testnet" or "preprod" => "https://cardano-preprod.blockfrost.io/api/v0",
            "preview" => "https://cardano-preview.blockfrost.io/api/v0",
            _ => "https://cardano-mainnet.blockfrost.io/api/v0"
        };
    }

    private HttpRequestMessage BuildBlockfrostRequest(string relativePath)
    {
        var endpoint = $"{ResolveBlockfrostApiBaseUrl()}/{relativePath.TrimStart('/')}";
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Add("project_id", _config.BlockfrostProjectId);
        return req;
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
            var last = _currentPath.Last().ToLowerInvariant();
            switch (last)
            {
                case "use":
                    result = _proxyAccount != null ? $"Active: {_proxyAccount}\n" : "None\n";
                    break;
                case "balance":
                    EnsureMethodAllowed("getBalance");
                    result = await TryGetLiveBalanceAsync()
                        ?? $"{_mockBalanceAda.ToString("0.000000", CultureInfo.InvariantCulture)} ADA (Mock)\n";
                    break;
                case "address":
                    result = _proxyAccount ?? "No proxy account selected.\n";
                    break;
                case "transactions":
                    EnsureMethodAllowed("getTransactions");
                    result = await TryGetLiveTransactionsAsync()
                        ?? (_mockTransactions.Count == 0
                            ? "No recent transactions.\n"
                            : string.Join('\n', _mockTransactions) + '\n');
                    break;
                case "status":
                    result = await BuildStatusAsync();
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
                    if (charSpan.Length == 0) throw new NinePProtocolException("Account ID required.");
                    _proxyAccount = charSpan.ToString();
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
            }

            if (_currentPath.Count == 1 && _currentPath[0] == "send")
            {
                if (HasLiveApi)
                {
                    if (charSpan.StartsWith("cbor:", StringComparison.OrdinalIgnoreCase))
                    {
                        EnsureMethodAllowed("submitTransaction");
                        var cborHex = charSpan.Slice(5).Trim().ToString();
                        var txHash = await SubmitSignedTransactionAsync(cborHex);
                        _mockTxCounter++;
                        _mockTransactions.Insert(0, $"ada-live-{_mockTxCounter:D6} hash={txHash} status=submitted");
                        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                    }
                    throw new NinePProtocolException("Node-side signing for Cardano is not supported via standard proxy. Use pre-signed CBOR: 'cbor:<hex>'.");
                }

                var (to, amount) = ParseTransferCommand(charSpan, "address:amount");
                if (_mockBalanceAda < amount)
                {
                    throw new NinePProtocolException("Insufficient funds.");
                }
                _mockBalanceAda -= amount;
                _mockTxCounter++;
                _mockTransactions.Insert(
                    0,
                    $"ada-mock-{_mockTxCounter:D6} to={to} amount={amount.ToString("0.######", CultureInfo.InvariantCulture)} status=confirmed");
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

    private async Task<string> SubmitSignedTransactionAsync(string cborHex)
    {
        byte[] txBytes;
        try
        {
            txBytes = Convert.FromHexString(cborHex);
        }
        catch (FormatException)
        {
            throw new NinePProtocolException("Invalid signed transaction hex.");
        }

        if (txBytes.Length == 0)
        {
            throw new NinePProtocolException("Signed transaction payload is empty.");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ResolveBlockfrostApiBaseUrl()}/tx/submit");
        req.Headers.Add("project_id", _config.BlockfrostProjectId);
        req.Content = new ByteArrayContent(txBytes);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/cbor");

        using var resp = await _httpClient.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var compact = body.Length > 180 ? body[..180] + "..." : body;
            throw new NinePProtocolException($"Cardano submit failed ({(int)resp.StatusCode}): {compact}");
        }

        return body.Trim().Trim('"');
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "cardano";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0);
        if (name == "use" || name == "send") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePPermissionDeniedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePPermissionDeniedException();
    public Task<Rcreate> CreateAsync(Tcreate tcreate) => throw new NinePPermissionDeniedException();

    private async Task<string?> TryGetLiveBalanceAsync()
    {
        if (!HasLiveApi || string.IsNullOrWhiteSpace(_proxyAccount))
        {
            return null;
        }

        try
        {
            using var req = BuildBlockfrostRequest($"addresses/{Uri.EscapeDataString(_proxyAccount)}");
            using var resp = await _httpClient.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("amount", out var amountArray) || amountArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var amount in amountArray.EnumerateArray())
            {
                var unit = amount.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() : null;
                if (!string.Equals(unit, "lovelace", StringComparison.Ordinal))
                {
                    continue;
                }

                var quantity = amount.TryGetProperty("quantity", out var quantityValue) ? quantityValue.GetString() : null;
                if (!decimal.TryParse(quantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lovelace))
                {
                    return null;
                }

                var ada = lovelace / LovelacePerAda;
                return $"{ada.ToString("0.000000", CultureInfo.InvariantCulture)} ADA (Live)\n";
            }

            return "0.000000 ADA (Live)\n";
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetLiveTransactionsAsync()
    {
        if (!HasLiveApi || string.IsNullOrWhiteSpace(_proxyAccount))
        {
            return null;
        }

        try
        {
            using var req = BuildBlockfrostRequest($"addresses/{Uri.EscapeDataString(_proxyAccount)}/transactions?count=10&page=1&order=desc");
            using var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var txLines = new List<string>();
            foreach (var tx in doc.RootElement.EnumerateArray())
            {
                if (!tx.TryGetProperty("tx_hash", out var txHashValue))
                {
                    continue;
                }

                var txHash = txHashValue.GetString();
                if (string.IsNullOrWhiteSpace(txHash))
                {
                    continue;
                }

                var blockHeight = tx.TryGetProperty("block_height", out var blockHeightValue)
                    ? blockHeightValue.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : "?";
                txLines.Add($"{txHash} block={blockHeight}");
            }

            return txLines.Count == 0
                ? "No recent transactions.\n"
                : string.Join('\n', txLines) + '\n';
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> BuildStatusAsync()
    {
        var builder = new StringBuilder();
        builder.Append("Network: ").Append(_config.Network).Append('\n');
        builder.Append("ActiveAccount: ").Append(_proxyAccount ?? "None").Append('\n');
        builder.Append("TrackedTx: ").Append(_mockTransactions.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');

        if (!HasLiveApi)
        {
            builder.Append("Mode: Mock\n");
            return builder.ToString();
        }

        builder.Append("Mode: Live (Blockfrost)\n");
        builder.Append("API: ").Append(ResolveBlockfrostApiBaseUrl()).Append('\n');
        return builder.ToString();
    }

    public INinePFileSystem Clone()
    {
        var clone = new CardanoFileSystem(_config, _vault, _authService, _certificate, _httpClient);
        clone._currentPath = new List<string>(_currentPath);
        clone._proxyAccount = _proxyAccount;
        clone._mockBalanceAda = _mockBalanceAda;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        return clone;
    }
}

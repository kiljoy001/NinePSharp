using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CardanoSharp.Wallet;
using CardanoSharp.Wallet.Enums;
using CardanoSharp.Wallet.Extensions.Models;
using CardanoSharp.Wallet.Utilities;
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
    private readonly HttpClient _httpClient;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedMnemonic;
    private string? _unlockedAddress;
    private decimal _mockBalanceAda = 100.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public bool DotU { get; set; }

    public CardanoFileSystem(CardanoBackendConfig config, ILuxVaultService vault, HttpClient? httpClient = null)
    {
        _config = config;
        _vault = vault;
        _httpClient = httpClient ?? SharedHttpClient;
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

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, GetQid(_currentPath), 0);

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
                files.Add(("balance", QidType.QTFILE));
                files.Add(("address", QidType.QTFILE));
                files.Add(("send", QidType.QTFILE));
                files.Add(("transactions", QidType.QTFILE));
                files.Add(("status", QidType.QTFILE));
            }
            else if (_currentPath[0] == "wallets")
            {
                files.Add(("create", QidType.QTFILE));
                files.Add(("import", QidType.QTFILE));
                files.Add(("unlock", QidType.QTFILE));
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
            switch (last)
            {
                case "unlock":
                    result = _unlockedAddress != null ? $"Unlocked: {_unlockedAddress}\n" : "Locked\n";
                    break;
                case "balance":
                    result = await TryGetLiveBalanceAsync()
                        ?? $"{_mockBalanceAda.ToString("0.000000", CultureInfo.InvariantCulture)} ADA (Mock)\n";
                    break;
                case "address":
                    result = _unlockedAddress ?? "No wallet unlocked.\n";
                    break;
                case "transactions":
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

                var mnemonicService = new MnemonicService();
                var mnemonic = mnemonicService.Generate(24);
                byte[] wordsBytes = Encoding.UTF8.GetBytes(mnemonic.Words);
                
                try {
                    var ciphertext = _vault.Encrypt(wordsBytes, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"ada_vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(wordsBytes);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:mnemonicWords
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim();
                    var parts = fullStr.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:mnemonicWords'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var mnemonicWords = parts[1];
                    byte[] wordsBytes = Encoding.UTF8.GetBytes(mnemonicWords);
                    try {
                        var ciphertext = _vault.Encrypt(wordsBytes, password);
                        byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                        var seed = _vault.DeriveSeed(password, idSalt);
                        var hiddenId = _vault.GenerateHiddenId(seed);
                        
                        File.WriteAllBytes(_vault.GetVaultPath($"ada_vault_{hiddenId}.vlt"), ciphertext);
                    }
                    finally {
                        Array.Clear(wordsBytes);
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

                byte[] idSalt = Encoding.UTF8.GetBytes("Cardano_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"ada_vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var wordsBytes = _vault.DecryptToBytes(encrypted, password);
                    if (wordsBytes != null)
                    {
                        try {
                            if (_unlockedMnemonic != null) Array.Clear(_unlockedMnemonic);
                            _unlockedMnemonic = GC.AllocateArray<byte>(wordsBytes.Length, pinned: true);
                            wordsBytes.CopyTo(_unlockedMnemonic, 0);

                            var derivedAddress = TryDeriveAddressFromMnemonic(wordsBytes);
                            if (string.IsNullOrWhiteSpace(derivedAddress))
                            {
                                var digest = SHA256.HashData(wordsBytes);
                                derivedAddress = $"addr1_mock_{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}";
                            }

                            _unlockedAddress = derivedAddress;
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        finally {
                            Array.Clear(wordsBytes);
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            if (_unlockedMnemonic == null || string.IsNullOrEmpty(_unlockedAddress))
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            if (HasLiveApi)
            {
                var command = Encoding.UTF8.GetString(twrite.Data.Span).Trim();
                if (!TryExtractSignedCborHex(command, out var cborHex))
                {
                    throw new NinePProtocolException("Live mode requires signed transaction CBOR: use 'cbor:<hex>'.");
                }

                var txHash = await SubmitSignedTransactionAsync(cborHex);
                _mockTxCounter++;
                _mockTransactions.Insert(0, $"ada-live-{_mockTxCounter:D6} hash={txHash} status=submitted");
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }

            var transfer = ParseTransferCommand(twrite.Data, "address:amount");
            if (_mockBalanceAda < transfer.Amount)
            {
                throw new NinePProtocolException("Insufficient funds.");
            }
            _mockBalanceAda -= transfer.Amount;
            _mockTxCounter++;
            _mockTransactions.Insert(
                0,
                $"ada-mock-{_mockTxCounter:D6} to={transfer.To} amount={transfer.Amount.ToString("0.######", CultureInfo.InvariantCulture)} status=confirmed");
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

    private static bool TryExtractSignedCborHex(string command, out string cborHex)
    {
        cborHex = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var value = command.Trim();
        if (value.StartsWith("cbor:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[5..].Trim();
        }

        if (!IsEvenLengthHex(value))
        {
            return false;
        }

        cborHex = value;
        return true;
    }

    private static bool IsEvenLengthHex(string value)
    {
        if (value.Length == 0 || (value.Length % 2) != 0)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
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

        var txHash = body.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(txHash))
        {
            throw new NinePProtocolException("Cardano submit returned an empty transaction hash.");
        }

        return txHash;
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "cardano";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "import" || name == "create" || name == "unlock") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    private async Task<string?> TryGetLiveBalanceAsync()
    {
        if (!HasLiveApi || string.IsNullOrWhiteSpace(_unlockedAddress))
        {
            return null;
        }

        try
        {
            using var req = BuildBlockfrostRequest($"addresses/{Uri.EscapeDataString(_unlockedAddress)}");
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
        if (!HasLiveApi || string.IsNullOrWhiteSpace(_unlockedAddress))
        {
            return null;
        }

        try
        {
            using var req = BuildBlockfrostRequest($"addresses/{Uri.EscapeDataString(_unlockedAddress)}/transactions?count=10&page=1&order=desc");
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
        builder.Append("Address: ").Append(_unlockedAddress ?? "None").Append('\n');
        builder.Append("TrackedTx: ").Append(_mockTransactions.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');

        if (!HasLiveApi)
        {
            builder.Append("Mode: Mock\n");
            return builder.ToString();
        }

        builder.Append("Mode: Live (Blockfrost)\n");
        builder.Append("API: ").Append(ResolveBlockfrostApiBaseUrl()).Append('\n');
        var latestBlock = await TryGetLatestBlockHashAsync();
        builder.Append("LatestBlock: ").Append(latestBlock ?? "unavailable").Append('\n');
        return builder.ToString();
    }

    private string? TryDeriveAddressFromMnemonic(ReadOnlySpan<byte> mnemonicBytes)
    {
        try
        {
            var mnemonicWords = Encoding.UTF8.GetString(mnemonicBytes).Trim();
            if (string.IsNullOrWhiteSpace(mnemonicWords))
            {
                return null;
            }

            var mnemonicService = new MnemonicService();
            var mnemonic = mnemonicService.Restore(mnemonicWords, WordLists.English);
            var master = mnemonic.GetMasterNode();

            var payment = master
                .Derive(PurposeType.Shelley)
                .Derive(CoinType.Ada)
                .Derive(0)
                .Derive(RoleType.ExternalChain)
                .Derive(0);

            var stake = master
                .Derive(PurposeType.Shelley)
                .Derive(CoinType.Ada)
                .Derive(0)
                .Derive(RoleType.Staking)
                .Derive(0);

            return AddressUtility.GetBaseAddress(payment.PublicKey, stake.PublicKey, ResolveNetworkType()).ToString();
        }
        catch
        {
            return null;
        }
    }

    private NetworkType ResolveNetworkType()
    {
        return (_config.Network ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "main" or "mainnet" => NetworkType.Mainnet,
            "testnet" => NetworkType.Testnet,
            "preprod" => NetworkType.Preprod,
            "preview" => NetworkType.Preview,
            _ => NetworkType.Mainnet
        };
    }

    private async Task<string?> TryGetLatestBlockHashAsync()
    {
        if (!HasLiveApi)
        {
            return null;
        }

        try
        {
            using var req = BuildBlockfrostRequest("blocks/latest");
            using var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("hash", out var hashValue)
                ? hashValue.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public INinePFileSystem Clone()
    {
        var clone = new CardanoFileSystem(_config, _vault, _httpClient);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedMnemonic != null)
        {
            clone._unlockedMnemonic = GC.AllocateArray<byte>(_unlockedMnemonic.Length, pinned: true);
            _unlockedMnemonic.CopyTo(clone._unlockedMnemonic, 0);
        }
        clone._unlockedAddress = _unlockedAddress;
        clone._mockBalanceAda = _mockBalanceAda;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        return clone;
    }
}

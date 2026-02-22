using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using StellarDotnetSdk;
using StellarDotnetSdk.Accounts;
using StellarDotnetSdk.Assets;
using StellarDotnetSdk.Operations;
using StellarDotnetSdk.Transactions;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using StellarServer = StellarDotnetSdk.Server;

namespace NinePSharp.Server.Backends;

public class StellarFileSystem : INinePFileSystem
{
    private readonly StellarBackendConfig _config;
    private readonly StellarServer? _server;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedPrivateKey;
    private string? _unlockedAddress;
    private decimal _mockBalanceXlm = 50.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public bool DotU { get; set; }

    public StellarFileSystem(StellarBackendConfig config, StellarServer? server, ILuxVaultService vault)
    {
        _config = config;
        _server = server;
        _vault = vault;
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
                files.Add(("network", QidType.QTFILE));
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
                    result = await TryGetLiveBalanceTextAsync()
                        ?? $"{_mockBalanceXlm.ToString("0.000000", CultureInfo.InvariantCulture)} XLM (Mock)\n";
                    break;
                case "address":
                    result = _unlockedAddress ?? "No wallet unlocked.\n";
                    break;
                case "transactions":
                    result = _mockTransactions.Count == 0
                        ? "No recent transactions.\n"
                        : string.Join('\n', _mockTransactions) + '\n';
                    break;
                case "status":
                    result = $"Horizon: {_config.HorizonUrl}\nAddress: {_unlockedAddress ?? "None"}\nTrackedTx: {_mockTransactions.Count}\nMode: {(_server != null ? "Live" : "Mock")}\n";
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

                var kp = KeyPair.Random();
                byte[] seedBytes = kp.SecretSeed != null ? Encoding.UTF8.GetBytes(kp.SecretSeed) : Array.Empty<byte>();
                
                try {
                    var ciphertext = _vault.Encrypt(seedBytes, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Stellar_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"xlm_vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(seedBytes);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:secretSeed
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim();
                    var parts = fullStr.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:secretSeed'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var secretSeed = parts[1];
                    byte[] seedBytes = Encoding.UTF8.GetBytes(secretSeed);
                    try { 
                        KeyPair.FromSecretSeed(secretSeed); 
                        var ciphertext = _vault.Encrypt(seedBytes, password);
                        byte[] idSalt = Encoding.UTF8.GetBytes("Stellar_Vault_ID_Salt_v1");
                        var seed = _vault.DeriveSeed(password, idSalt);
                        var hiddenId = _vault.GenerateHiddenId(seed);
                        
                        File.WriteAllBytes(_vault.GetVaultPath($"xlm_vault_{hiddenId}.vlt"), ciphertext);
                    } 
                    catch { throw new NinePProtocolException("Invalid secret seed."); }
                    finally {
                        Array.Clear(seedBytes);
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

                byte[] idSalt = Encoding.UTF8.GetBytes("Stellar_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"xlm_vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    var seedBytes = _vault.DecryptToBytes(encrypted, password);
                    if (seedBytes != null)
                    {
                        try {
                            if (_unlockedPrivateKey != null) Array.Clear(_unlockedPrivateKey);
                            _unlockedPrivateKey = GC.AllocateArray<byte>(seedBytes.Length, pinned: true);
                            seedBytes.CopyTo(_unlockedPrivateKey, 0);
                            
                            var secretSeed = Encoding.UTF8.GetString(seedBytes);
                            var kp = KeyPair.FromSecretSeed(secretSeed);
                            _unlockedAddress = kp.AccountId;
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        finally {
                            Array.Clear(seedBytes);
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            if (_unlockedPrivateKey == null || string.IsNullOrEmpty(_unlockedAddress))
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            var transfer = ParseTransferCommand(twrite.Data, "address:amount");
            if (_server != null)
            {
                var txHash = await SendLiveTransferAsync(transfer.To, transfer.Amount);
                _mockTxCounter++;
                _mockTransactions.Insert(
                    0,
                    $"{txHash} to={transfer.To} amount={transfer.Amount.ToString("0.######", CultureInfo.InvariantCulture)} status=submitted");
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }

            if (_mockBalanceXlm < transfer.Amount)
            {
                throw new NinePProtocolException("Insufficient funds.");
            }

            _mockBalanceXlm -= transfer.Amount;
            _mockTxCounter++;
            _mockTransactions.Insert(
                0,
                $"xlm-mock-{_mockTxCounter:D6} to={transfer.To} amount={transfer.Amount.ToString("0.######", CultureInfo.InvariantCulture)} status=confirmed");
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

    private async Task<string?> TryGetLiveBalanceTextAsync()
    {
        if (_server == null || string.IsNullOrWhiteSpace(_unlockedAddress))
        {
            return null;
        }

        try
        {
            var account = await _server.Accounts.Account(_unlockedAddress);
            var native = account.Balances?.FirstOrDefault(b => string.Equals(b.AssetType, "native", StringComparison.OrdinalIgnoreCase));
            if (native == null || !decimal.TryParse(native.BalanceString, NumberStyles.Number, CultureInfo.InvariantCulture, out var xlm))
            {
                return null;
            }

            return $"{xlm.ToString("0.000000", CultureInfo.InvariantCulture)} XLM (Live)\n";
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> SendLiveTransferAsync(string destination, decimal amountXlm)
    {
        if (_server == null || _unlockedPrivateKey == null)
        {
            throw new NinePProtocolException("Stellar server is not configured.");
        }

        if (amountXlm <= 0)
        {
            throw new NinePProtocolException("Transfer amount must be positive.");
        }

        try
        {
            var sourceSeed = Encoding.UTF8.GetString(_unlockedPrivateKey);
            var source = KeyPair.FromSecretSeed(sourceSeed);
            var sourceAccount = await _server.Accounts.Account(source.AccountId);
            var destinationAccount = KeyPair.FromAccountId(destination);
            var network = _config.UsePublicNetwork ? Network.Public() : Network.Test();

            var tx = new TransactionBuilder(sourceAccount)
                .SetFee(100)
                .AddTimeBounds(new TimeBounds(TimeSpan.FromMinutes(5)))
                .AddOperation(new PaymentOperation(
                    destinationAccount,
                    Asset.Create("native", null, null),
                    amountXlm.ToString("0.######", CultureInfo.InvariantCulture),
                    null!))
                .Build();

            tx.Sign(source, network);
            var submitted = await _server.SubmitTransaction(tx);
            if (submitted == null || !submitted.IsSuccess || string.IsNullOrWhiteSpace(submitted.Hash))
            {
                throw new NinePProtocolException("Stellar transfer failed.");
            }

            return submitted.Hash!;
        }
        catch (NinePProtocolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NinePProtocolException($"Stellar transfer failed: {ex.Message}");
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "stellar";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "import" || name == "create" || name == "unlock") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new StellarFileSystem(_config, _server, _vault);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedPrivateKey != null)
        {
            clone._unlockedPrivateKey = GC.AllocateArray<byte>(_unlockedPrivateKey.Length, pinned: true);
            _unlockedPrivateKey.CopyTo(clone._unlockedPrivateKey, 0);
        }
        clone._unlockedAddress = _unlockedAddress;
        clone._mockBalanceXlm = _mockBalanceXlm;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        return clone;
    }
}

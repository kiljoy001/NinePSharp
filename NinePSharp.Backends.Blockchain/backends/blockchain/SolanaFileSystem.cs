using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Models;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
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
    private readonly IRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    
    private byte[]? _unlockedPrivateKey;
    private string? _unlockedAddress;
    private decimal _mockBalanceSol = 25.000000m;
    private List<string> _mockTransactions = new();
    private long _mockTxCounter;

    public bool DotU { get; set; }

    public SolanaFileSystem(SolanaBackendConfig config, IRpcClient? rpcClient, ILuxVaultService vault)
    {
        _config = config;
        _rpcClient = rpcClient;
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
        return new Qid(type, 0, DeterministicHash.GetStableHash64(pathStr));
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (!IsDirectory(tempPath) && name != "..")
            {
                if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                break;
            }

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
                files.Add(("mempool", QidType.QTFILE));
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
            if (last == "unlock")
            {
                result = _unlockedAddress != null ? $"Unlocked: {_unlockedAddress}\n" : "Locked\n";
            }
            else if (last == "balance")
            {
                result = await TryGetLiveBalanceTextAsync()
                    ?? $"{_mockBalanceSol.ToString("0.000000", CultureInfo.InvariantCulture)} SOL (Mock)\n";
            }
            else if (last == "address")
            {
                result = _unlockedAddress ?? "No wallet unlocked.\n";
            }
            else if (last == "transactions")
            {
                result = _mockTransactions.Count == 0
                    ? "No recent transactions.\n"
                    : string.Join('\n', _mockTransactions) + '\n';
            }
            else if (last == "status")
            {
                result = $"RPC: {_config.RpcUrl}\nAddress: {_unlockedAddress ?? "None"}\nTrackedTx: {_mockTransactions.Count}\nMode: {(_rpcClient != null ? "Live" : "Mock")}\n";
            }
            else if (last == "network")
            {
                result = _config.RpcUrl?.Contains("mainnet") == true ? "Mainnet\n" : "Devnet/Testnet\n";
            }
            else if (last == "mempool")
            {
                if (_rpcClient != null) {
                    try {
                        // var count = await _rpcClient.GetMaxRetransmitLinesAsync(); // Or similar
                        result = $"Pending transactions info not directly available via basic RPC.\n";
                    } catch { result = "Unavailable\n"; }
                } else result = "Mock Mempool: 0 pending\n";
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

                var wallet = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
                var account = wallet.GetAccount(0);
                byte[] keyMaterial = Encoding.UTF8.GetBytes(SerializeKeyMaterial(account.PrivateKey.Key, account.PublicKey.Key));
                
                try {
                    var ciphertext = _vault.Encrypt(keyMaterial, password);
                    byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                    var seed = _vault.DeriveSeed(password, idSalt);
                    var hiddenId = _vault.GenerateHiddenId(seed);
                    
                    File.WriteAllBytes(_vault.GetVaultPath($"sol_vault_{hiddenId}.vlt"), ciphertext);
                }
                finally {
                    Array.Clear(keyMaterial);
                }
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            else if (_currentPath[1] == "import")
            {
                // Format: password:base58PrivKey[:base58PublicKey]
                var bytes = twrite.Data.Span;
                char[] chars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bytes), pinned: true);
                try {
                    Encoding.UTF8.GetChars(bytes, chars);
                    string fullStr = new string(chars).Trim();
                    var parts = fullStr.Split(':', 3);
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) 
                        throw new NinePProtocolException("Invalid format or missing password. Use 'password:base58PrivKey[:base58PublicKey]'");

                    using var password = new SecureString();
                    foreach (char c in parts[0]) password.AppendChar(c);
                    password.MakeReadOnly();

                    var privKeyBase58 = parts[1];
                    var pubKeyBase58 = parts.Length == 3 ? parts[2] : string.Empty;
                    if (string.IsNullOrWhiteSpace(pubKeyBase58))
                    {
                        if (!TryExtractPublicKeyFromPrivate(privKeyBase58, out pubKeyBase58))
                        {
                            throw new NinePProtocolException("Invalid Solana private key. Use 'password:base58PrivKey:base58PublicKey' when public key cannot be derived.");
                        }
                    }

                    try {
                        _ = new Account(privKeyBase58, pubKeyBase58);
                        byte[] keyMaterial = Encoding.UTF8.GetBytes(SerializeKeyMaterial(privKeyBase58, pubKeyBase58));
                        try {
                            var ciphertext = _vault.Encrypt(keyMaterial, password);
                            byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                            var seed = _vault.DeriveSeed(password, idSalt);
                            var hiddenId = _vault.GenerateHiddenId(seed);
                            
                            File.WriteAllBytes(_vault.GetVaultPath($"sol_vault_{hiddenId}.vlt"), ciphertext);
                        }
                        finally {
                            Array.Clear(keyMaterial);
                        }
                        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                    }
                    catch (NinePProtocolException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw new NinePProtocolException("Invalid Solana private key.");
                    }
                }
                finally {
                    Array.Clear(chars);
                }
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

                byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
                var seed = _vault.DeriveSeed(password, idSalt);
                var hiddenId = _vault.GenerateHiddenId(seed);
                var vaultFile = _vault.GetVaultPath($"sol_vault_{hiddenId}.vlt");

                if (File.Exists(vaultFile))
                {
                    var encrypted = File.ReadAllBytes(vaultFile);
                    using var privKey = _vault.DecryptToBytes(encrypted, password);
                    if (privKey != null)
                    {
                        try {
                            var keyMaterialText = Encoding.UTF8.GetString(privKey.Span);
                            var (privateKeyText, storedPublicKeyText) = ParseKeyMaterial(keyMaterialText);
                            if (string.IsNullOrWhiteSpace(storedPublicKeyText))
                            {
                                TryExtractPublicKeyFromPrivate(privateKeyText, out storedPublicKeyText);
                            }
                            if (string.IsNullOrWhiteSpace(storedPublicKeyText))
                            {
                                throw new NinePProtocolException("Wallet key material is invalid.");
                            }

                            byte[] privateKeyBytes = Encoding.UTF8.GetBytes(privateKeyText);
                            if (_unlockedPrivateKey != null) Array.Clear(_unlockedPrivateKey);
                            _unlockedPrivateKey = GC.AllocateArray<byte>(privateKeyBytes.Length, pinned: true);
                            privateKeyBytes.CopyTo(_unlockedPrivateKey, 0);
                            Array.Clear(privateKeyBytes);

                            var account = new Account(privateKeyText, storedPublicKeyText);
                            var address = account.PublicKey.Key;
                            if (string.IsNullOrWhiteSpace(address))
                            {
                                throw new NinePProtocolException("Wallet key material is invalid.");
                            }

                            _unlockedAddress = address;
                            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                        }
                        catch (NinePProtocolException)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            throw new NinePProtocolException("Wallet not found or invalid password.");
                        }
                    }
                }
                throw new NinePProtocolException("Wallet not found or invalid password.");
            }
        }

        if (_currentPath.Count == 1 && _currentPath[0] == "send")
        {
            if (_unlockedPrivateKey == null)
            {
                throw new InvalidOperationException("Wallet not unlocked. Write password to /wallets/unlock first.");
            }

            var transfer = ParseTransferCommand(twrite.Data, "address:amount");
            if (_rpcClient != null)
            {
                var signature = await SendLiveTransferAsync(transfer.To, transfer.Amount);
                _mockTxCounter++;
                _mockTransactions.Insert(
                    0,
                    $"{signature} to={transfer.To} amount={transfer.Amount.ToString("0.######", CultureInfo.InvariantCulture)} status=submitted");
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }

            if (_mockBalanceSol < transfer.Amount)
            {
                throw new NinePProtocolException("Insufficient funds.");
            }

            _mockBalanceSol -= transfer.Amount;
            _mockTxCounter++;
            _mockTransactions.Insert(
                0,
                $"sol-mock-{_mockTxCounter:D6} to={transfer.To} amount={transfer.Amount.ToString("0.######", CultureInfo.InvariantCulture)} status=confirmed");
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
        if (_rpcClient == null || string.IsNullOrWhiteSpace(_unlockedAddress))
        {
            return null;
        }

        try
        {
            var balance = await _rpcClient.GetBalanceAsync(_unlockedAddress, Commitment.Finalized);
            if (!balance.WasSuccessful || balance.Result == null)
            {
                return null;
            }

            decimal sol = balance.Result.Value / 1_000_000_000m;
            return $"{sol.ToString("0.000000", CultureInfo.InvariantCulture)} SOL (Live)\n";
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> SendLiveTransferAsync(string destination, decimal amountSol)
    {
        if (_rpcClient == null || _unlockedPrivateKey == null)
        {
            throw new NinePProtocolException("Solana RPC client is not configured.");
        }

        string privateKeyText = Encoding.UTF8.GetString(_unlockedPrivateKey);
        ulong lamports = ToLamports(amountSol);

        try
        {
            var sender = new Account(privateKeyText, _unlockedAddress ?? string.Empty);
            var latest = await _rpcClient.GetLatestBlockHashAsync(Commitment.Finalized);
            if (!latest.WasSuccessful || latest.Result?.Value?.Blockhash == null)
            {
                throw new NinePProtocolException($"Failed to fetch latest block hash: {latest.Reason}");
            }

            var tx = new Transaction
            {
                FeePayer = sender.PublicKey,
                RecentBlockHash = latest.Result.Value.Blockhash
            };

            tx.Add(SystemProgram.Transfer(sender.PublicKey, new PublicKey(destination), lamports));
            byte[] signed = tx.Build(sender);

            var submitted = await _rpcClient.SendTransactionAsync(signed, false, Commitment.Confirmed);
            if (!submitted.WasSuccessful || string.IsNullOrWhiteSpace(submitted.Result))
            {
                throw new NinePProtocolException($"Solana transfer failed: {submitted.Reason}");
            }

            return submitted.Result;
        }
        catch (NinePProtocolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NinePProtocolException($"Solana transfer failed: {ex.Message}");
        }
    }

    private static ulong ToLamports(decimal amountSol)
    {
        if (amountSol <= 0)
        {
            throw new NinePProtocolException("Transfer amount must be positive.");
        }

        decimal lamports = amountSol * 1_000_000_000m;
        if (lamports > ulong.MaxValue)
        {
            throw new NinePProtocolException("Transfer amount is too large.");
        }

        return decimal.ToUInt64(decimal.Round(lamports, 0, MidpointRounding.ToZero));
    }

    private static string SerializeKeyMaterial(string privateKey, string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return privateKey;
        }

        return $"{privateKey}:{publicKey}";
    }

    private static (string PrivateKey, string? PublicKey) ParseKeyMaterial(string keyMaterial)
    {
        var parts = keyMaterial.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            return (parts[0], parts[1]);
        }

        return (keyMaterial, null);
    }

    private static bool TryExtractPublicKeyFromPrivate(string privateKeyBase58, out string publicKeyBase58)
    {
        publicKeyBase58 = string.Empty;
        byte[]? decodedPrivate = null;
        byte[]? publicKeyBytes = null;
        try
        {
            decodedPrivate = Encoders.Base58.DecodeData(privateKeyBase58);
            if (decodedPrivate.Length < 64)
            {
                return false;
            }

            publicKeyBytes = decodedPrivate.AsSpan(decodedPrivate.Length - 32, 32).ToArray();
            publicKeyBase58 = Encoders.Base58.EncodeData(publicKeyBytes);
            return !string.IsNullOrWhiteSpace(publicKeyBase58);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (publicKeyBytes != null)
            {
                Array.Clear(publicKeyBytes);
            }

            if (decodedPrivate != null)
            {
                Array.Clear(decodedPrivate);
            }
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "solana";
        bool isDir = IsDirectory(_currentPath);
        uint mode = 0644;
        if (isDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED;
        else if (name == "import" || name == "create" || name == "unlock") mode = 0666;

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var name = _currentPath.LastOrDefault() ?? "solana";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu : 0644;
        if (name == "import" || name == "create" || name == "unlock") mode = 0666;

        var qid = GetQid(_currentPath);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode);
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SolanaFileSystem(_config, _rpcClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        if (_unlockedPrivateKey != null)
        {
            clone._unlockedPrivateKey = GC.AllocateArray<byte>(_unlockedPrivateKey.Length, pinned: true);
            _unlockedPrivateKey.CopyTo(clone._unlockedPrivateKey, 0);
        }
        clone._unlockedAddress = _unlockedAddress;
        clone._mockBalanceSol = _mockBalanceSol;
        clone._mockTransactions = new List<string>(_mockTransactions);
        clone._mockTxCounter = _mockTxCounter;
        return clone;
    }
}

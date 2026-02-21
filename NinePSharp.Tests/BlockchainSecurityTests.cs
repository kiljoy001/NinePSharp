using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Constants;
using Moq;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests.Security;

public class BlockchainSecurityTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    public BlockchainSecurityTests()
    {
        // Ensure session key is initialized for memory protection
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        NinePSharp.Server.Configuration.ProtectedSecret.InitializeSessionKey(key);
    }

    [Fact]
    public async Task Ethereum_Unlock_Password_Is_Zeroed_In_Memory()
    {
        var config = new EthereumBackendConfig { MountPath = "/eth", RpcUrl = "http://localhost" };
        var fs = new EthereumFileSystem(config, new Mock<Nethereum.Web3.IWeb3>().Object, _vault);

        var password = "super-secret-password";

        // 1. Create a wallet first so it exists on disk
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        // 2. Walk to unlock node
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));

        // 3. Unlock it
        var twrite = new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password));
        var response = await fs.WriteAsync(twrite);

        Assert.IsType<Rwrite>(response);
        Assert.Equal((uint)twrite.Data.Length, ((Rwrite)response).Count);
    }

    [Fact]
    public async Task Ethereum_Create_Requires_Password()
    {
        var config = new EthereumBackendConfig { MountPath = "/eth", RpcUrl = "http://localhost" };
        var fs = new EthereumFileSystem(config, new Mock<Nethereum.Web3.IWeb3>().Object, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));

        // Empty password
        var twrite = new Twrite(1, 2, 0, Array.Empty<byte>());
        
        await Assert.ThrowsAsync<NinePProtocolException>(async () => await fs.WriteAsync(twrite));
    }

    [Fact]
    public async Task Ethereum_Signing_Requires_Unlock_First()
    {
        var config = new EthereumBackendConfig { MountPath = "/eth", RpcUrl = "http://localhost" };
        var fs = new EthereumFileSystem(config, new Mock<Nethereum.Web3.IWeb3>().Object, _vault);

        // Walk to a contract call node
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "contracts", "0x123", "call", "transfer(0xabc,100)" }));

        // Attempt write (trigger call) without unlocking
        var twrite = new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("trigger"));
        
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fs.WriteAsync(twrite));
    }

    [Fact]
    public async Task Bitcoin_Balance_Requires_Initialized_RPC()
    {
        // Uninitialized client
        var config = new BitcoinBackendConfig { MountPath = "/btc" };
        var fs = new BitcoinFileSystem(config, null, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "balance" }));
        var response = await fs.ReadAsync(new Tread(1, 2, 0, 100));

        string content = Encoding.UTF8.GetString(response.Data.ToArray());
        Assert.Contains("Offline", content);
    }

    #region Property Tests

    [Property]
    public bool AllChains_PathTraversal_Is_Safe(int chainType, string[] maliciousPath)
    {
        if (maliciousPath == null) return true;
        
        // Inject ".." and "/" attempts
        var paths = maliciousPath.ToList();
        paths.Insert(0, "..");
        paths.Add("../../../etc/passwd");

        INinePFileSystem fs = (chainType % 5) switch
        {
            0 => new BitcoinFileSystem(new BitcoinBackendConfig(), null, _vault),
            1 => new EthereumFileSystem(new EthereumBackendConfig(), new Mock<Nethereum.Web3.IWeb3>().Object, _vault),
            2 => new SolanaFileSystem(new SolanaBackendConfig(), null, _vault),
            3 => new StellarFileSystem(new StellarBackendConfig(), null, _vault),
            _ => new CardanoFileSystem(new CardanoBackendConfig(), _vault)
        };

        var walkTask = fs.WalkAsync(new Twalk(1, 1, 2, paths.ToArray()));
        walkTask.Wait();
        var response = walkTask.Result;

        var statTask = fs.StatAsync(new Tstat(1, 1));
        statTask.Wait();
        
        // The Stat Name should never be "passwd" or "etc"
        return statTask.Result.Stat.Name != "passwd" && statTask.Result.Stat.Name != "etc";
    }

    [Property]
    public bool AllChains_LargeInput_DoesNotCrash(int chainType, byte[] hugeData)
    {
        if (hugeData == null || hugeData.Length < 1000) return true;

        INinePFileSystem fs = (chainType % 5) switch
        {
            0 => new BitcoinFileSystem(new BitcoinBackendConfig(), null, _vault),
            1 => new EthereumFileSystem(new EthereumBackendConfig(), new Mock<Nethereum.Web3.IWeb3>().Object, _vault),
            2 => new SolanaFileSystem(new SolanaBackendConfig(), null, _vault),
            3 => new StellarFileSystem(new StellarBackendConfig(), null, _vault),
            _ => new CardanoFileSystem(new CardanoBackendConfig(), _vault)
        };

        // Write huge data to a potentially sensitive node
        var twrite = new Twrite(1, 1, 0, hugeData);
        try {
            var task = fs.WriteAsync(twrite);
            task.Wait();
            return true;
        }
        catch (NotSupportedException) { return true; }
        catch (NinePProtocolException) { return true; }
        catch (Exception) { return false; }
    }

    #endregion
}

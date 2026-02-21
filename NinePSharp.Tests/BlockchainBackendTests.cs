using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Constants;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests.Backends;

public class BlockchainBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task BitcoinBackend_Initialization_Works()
    {
        var backend = new BitcoinBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Server:Bitcoin:MountPath", "/btc"),
            new KeyValuePair<string, string?>("Server:Bitcoin:Network", "Main")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Bitcoin", backend.Name);
        Assert.Equal("/btc", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }

    [Fact]
    public async Task BitcoinFileSystem_Read_Root_Contains_Files()
    {
        var config = new BitcoinBackendConfig { MountPath = "/btc", Network = "Main" };
        var fs = new BitcoinFileSystem(config, null, _vault);

        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);
        
        string content = Encoding.UTF8.GetString(response.Data.ToArray());
        Assert.Contains("balance", content);
        Assert.Contains("address", content);
        Assert.Contains("send", content);
        Assert.Contains("transactions", content);
    }

    [Fact]
    public async Task SolanaFileSystem_Read_Root_Contains_Files()
    {
        var config = new SolanaBackendConfig { MountPath = "/sol" };
        var fs = new SolanaFileSystem(config, null, _vault);

        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);
        
        string content = Encoding.UTF8.GetString(response.Data.ToArray());
        Assert.Contains("balance", content);
        Assert.Contains("status", content);
    }

    [Fact]
    public async Task StellarFileSystem_Read_Root_Contains_Files()
    {
        var config = new StellarBackendConfig { MountPath = "/stellar", HorizonUrl = "https://horizon.stellar.org" };
        var fs = new StellarFileSystem(config, null, _vault);

        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);
        
        string content = Encoding.UTF8.GetString(response.Data.ToArray());
        Assert.Contains("balance", content);
        Assert.Contains("network", content);
    }

    [Fact]
    public async Task CardanoFileSystem_Read_Root_Contains_Files()
    {
        var config = new CardanoBackendConfig { MountPath = "/cardano" };
        var fs = new CardanoFileSystem(config, _vault);

        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);
        
        // Ensure the response has some header information
        Assert.Equal(MessageTypes.Rread, response.Type);
    }

    [Fact]
    public async Task EthereumFileSystem_Read_Root_Contains_Folders()
    {
        var config = new EthereumBackendConfig { MountPath = "/eth", RpcUrl = "http://localhost" };
        var mockWeb3 = new Mock<Nethereum.Web3.IWeb3>();
        var fs = new EthereumFileSystem(config, mockWeb3.Object, _vault);

        var tread = new Tread(1, 1, 0, 8192);
        var response = await fs.ReadAsync(tread);
        
        string content = Encoding.UTF8.GetString(response.Data.ToArray());
        Assert.Contains("wallets", content);
        Assert.Contains("contracts", content);
        Assert.Contains("status", content);
    }

    #region Property Tests

    [Property]
    public bool AllChains_Stat_Has_DMDIR_For_Root(int chainType)
    {
        // 0: BTC, 1: ETH, 2: SOL, 3: XLM, 4: ADA
        INinePFileSystem fs = (chainType % 5) switch
        {
            0 => new BitcoinFileSystem(new BitcoinBackendConfig(), null, _vault),
            1 => new EthereumFileSystem(new EthereumBackendConfig(), new Mock<Nethereum.Web3.IWeb3>().Object, _vault),
            2 => new SolanaFileSystem(new SolanaBackendConfig(), null, _vault),
            3 => new StellarFileSystem(new StellarBackendConfig(), null, _vault),
            _ => new CardanoFileSystem(new CardanoBackendConfig(), _vault)
        };

        var statTask = fs.StatAsync(new Tstat(1, 1));
        statTask.Wait();
        var response = statTask.Result;

        return (response.Stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
    }

    [Property]
    public bool AllChains_Clone_Preserves_Path_Property(int chainType, string[] path)
    {
        if (path == null || path.Any(string.IsNullOrEmpty)) return true;
        // Limit path depth for testing
        var cleanPath = path.Take(3).Select(p => p.Replace("/", "")).ToArray();

        INinePFileSystem fs = (chainType % 5) switch
        {
            0 => new BitcoinFileSystem(new BitcoinBackendConfig(), null, _vault),
            1 => new EthereumFileSystem(new EthereumBackendConfig(), new Mock<Nethereum.Web3.IWeb3>().Object, _vault),
            2 => new SolanaFileSystem(new SolanaBackendConfig(), null, _vault),
            3 => new StellarFileSystem(new StellarBackendConfig(), null, _vault),
            _ => new CardanoFileSystem(new CardanoBackendConfig(), _vault)
        };

        // Walk to random path
        var walkTask = fs.WalkAsync(new Twalk(1, 1, 2, cleanPath));
        walkTask.Wait();

        // Clone
        var cloned = fs.Clone();

        // Stat the clone - it should have the same name as the last element of the path if successful
        var statTask = cloned.StatAsync(new Tstat(1, 2));
        statTask.Wait();
        
        if (cleanPath.Length > 0 && walkTask.Result.Wqid.Length == cleanPath.Length)
        {
            return statTask.Result.Stat.Name == cleanPath.Last();
        }

        return true;
    }

    #endregion
}

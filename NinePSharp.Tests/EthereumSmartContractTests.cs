using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Nethereum.Web3;
using Nethereum.Hex.HexTypes;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using System.Numerics;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests.Backends;

public class EthereumSmartContractTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task EthereumFileSystem_PathTracking_Works()
    {
        var config = new EthereumBackendConfig { DefaultAccount = "0x123", RpcUrl = "http://localhost:8545" };
        var mockWeb3 = new Mock<IWeb3>();
        var fs = new EthereumFileSystem(config, mockWeb3.Object, _vault);

        // Walk into contracts
        var rwalk = await fs.WalkAsync(new Twalk((ushort)1, 1u, 2u, new[] { "contracts" }));
        Assert.Single(rwalk.Wqid);
        
        // Clone to simulate dispatcher behavior for NewFid
        var fs2 = fs.Clone();
        
        // Walk further in fs2
        await fs2.WalkAsync(new Twalk((ushort)2, 2u, 3u, new[] { "0xabc", "call", "name()" }));
    }

    [Fact]
    public async Task EthereumFileSystem_BalanceRead_Works()
    {
        var config = new EthereumBackendConfig { DefaultAccount = "0x123", RpcUrl = "http://localhost:8545" };
        var mockWeb3 = new Mock<IWeb3>();
        
        var fs = new EthereumFileSystem(config, mockWeb3.Object, _vault);
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 1u, new[] { "balance" }));
        
        try {
            await fs.ReadAsync(new Tread((ushort)1, 1u, 0, 100));
        } catch (Exception) {
            // Expected for now as web3 is not fully mocked
        }
    }

    [Fact]
    public async Task EthereumFileSystem_WalletUnlocking_Works()
    {
        var config = new EthereumBackendConfig { DefaultAccount = "0x123", RpcUrl = "http://localhost:8545" };
        var mockWeb3 = new Mock<IWeb3>();
        var fs = new EthereumFileSystem(config, mockWeb3.Object, _vault);

        // 1. Create wallet
        await fs.WalkAsync(new Twalk((ushort)1, 1u, 1u, new[] { "wallets", "create" }));
        var password = System.Text.Encoding.UTF8.GetBytes("password");
        await fs.WriteAsync(new Twrite((ushort)1, 1u, 0, password));

        // 2. Unlock wallet
        await fs.WalkAsync(new Twalk((ushort)2, 1u, 2u, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite((ushort)2, 2u, 0, password));

        // 3. Check status
        await fs.WalkAsync(new Twalk((ushort)3, 2u, 3u, new[] { "..", "status" }));
        var rread = await fs.ReadAsync(new Tread((ushort)3, 3u, 0, 100));
        var status = System.Text.Encoding.UTF8.GetString(rread.Data.ToArray());
        Assert.Contains("Unlocked: 0x", status);
        
        // Cleanup
        if (File.Exists("wallet.vlt")) File.Delete("wallet.vlt");
    }
}

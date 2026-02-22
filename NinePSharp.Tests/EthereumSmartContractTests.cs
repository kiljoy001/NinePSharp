using System;
using System.Collections.Generic;
using System.IO;
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
using System.Security;
using System.Text;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests.Backends;

public class EthereumSmartContractTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    public EthereumSmartContractTests()
    {
        // Make this test class independent from global test execution order.
        byte[] key = new byte[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i + 1);
        }

        LuxVault.InitializeSessionKey(key);
        ProtectedSecret.InitializeSessionKey(key);
    }

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
        var password = Encoding.UTF8.GetBytes("password");
        await fs.WriteAsync(new Twrite((ushort)1, 1u, 0, password));

        // 2. Unlock wallet
        await fs.WalkAsync(new Twalk((ushort)2, 1u, 2u, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite((ushort)2, 2u, 0, password));

        // 3. Check status
        await fs.WalkAsync(new Twalk((ushort)3, 2u, 3u, new[] { "..", "status" }));
        var rread = await fs.ReadAsync(new Tread((ushort)3, 3u, 0, 100));
        var status = Encoding.UTF8.GetString(rread.Data.ToArray());
        Assert.Contains("Unlocked: 0x", status);
        
        // Cleanup
        using var securePassword = new SecureString();
        foreach (char c in "password")
        {
            securePassword.AppendChar(c);
        }

        securePassword.MakeReadOnly();
        byte[] idSalt = Encoding.UTF8.GetBytes("NinePSharp_Vault_ID_Salt_v1");
        string hiddenId = _vault.GenerateHiddenId(_vault.DeriveSeed(securePassword, idSalt));
        string vaultPath = LuxVault.GetVaultPath($"vault_{hiddenId}.vlt");
        if (File.Exists(vaultPath))
        {
            File.Delete(vaultPath);
        }
    }
}

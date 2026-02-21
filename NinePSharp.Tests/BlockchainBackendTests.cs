using System;
using System.Threading.Tasks;
using Xunit;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;

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
            new KeyValuePair<string, string>("Server:Bitcoin:MountPath", "/btc"),
            new KeyValuePair<string, string>("Server:Bitcoin:Network", "Main")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Bitcoin", backend.Name);
        Assert.Equal("/btc", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }

    [Fact]
    public async Task SolanaBackend_Initialization_Works()
    {
        var backend = new SolanaBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Server:Solana:MountPath", "/sol"),
            new KeyValuePair<string, string>("Server:Solana:RpcUrl", "https://api.mainnet-beta.solana.com")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Solana", backend.Name);
        Assert.Equal("/sol", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }

    [Fact]
    public async Task StellarBackend_Initialization_Works()
    {
        var backend = new StellarBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Server:Stellar:MountPath", "/stellar"),
            new KeyValuePair<string, string>("Server:Stellar:HorizonUrl", "https://horizon.stellar.org")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Stellar", backend.Name);
        Assert.Equal("/stellar", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }

    [Fact]
    public async Task CardanoBackend_Initialization_Works()
    {
        var backend = new CardanoBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Server:Cardano:MountPath", "/cardano"),
            new KeyValuePair<string, string>("Server:Cardano:Network", "Mainnet")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Cardano", backend.Name);
        Assert.Equal("/cardano", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }
}

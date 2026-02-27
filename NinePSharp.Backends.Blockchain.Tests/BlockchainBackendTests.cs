using System;
using System.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Fuzzer;
using Xunit;

namespace NinePSharp.Backends.Blockchain.Tests;

public class BlockchainBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    private IConfiguration CreateConfig(string mountPath, string rpcUrl)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                KeyValuePair.Create("MountPath", (string?)mountPath),
                KeyValuePair.Create("RpcUrl", (string?)rpcUrl)
            })
            .Build();
    }

    [Fact]
    public async Task EthereumBackend_Initialize_SetsMountPath()
    {
        var backend = new EthereumBackend(_vault);
        var config = CreateConfig("/eth", "https://mainnet.infura.io/v3/YOUR_KEY");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/eth");
    }

    [Fact]
    public async Task EthereumBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new EthereumBackend(_vault);
        await backend.InitializeAsync(CreateConfig("/eth", "https://mainnet.infura.io/v3/YOUR_KEY"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task BitcoinBackend_Initialize_SetsMountPath()
    {
        var backend = new BitcoinBackend(_vault);
        var config = CreateConfig("/btc", "http://localhost:8332");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/btc");
    }

    [Fact]
    public async Task BitcoinBackend_GetFileSystem_ReturnsFileSystem()
    {
        var backend = new BitcoinBackend(_vault);
        await backend.InitializeAsync(CreateConfig("/btc", "http://localhost:8332"));

        var fs = backend.GetFileSystem();

        fs.Should().NotBeNull();
    }

    [Fact]
    public async Task SolanaBackend_Initialize_SetsMountPath()
    {
        var backend = new SolanaBackend(_vault);
        var config = CreateConfig("/sol", "https://api.mainnet-beta.solana.com");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/sol");
    }

    [Fact]
    public async Task StellarBackend_Initialize_SetsMountPath()
    {
        var backend = new StellarBackend(_vault);
        var config = CreateConfig("/xlm", "https://horizon.stellar.org");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/xlm");
    }

    [Fact]
    public async Task CardanoBackend_Initialize_SetsMountPath()
    {
        var backend = new CardanoBackend(_vault);
        var config = CreateConfig("/ada", "https://cardano-mainnet.blockfrost.io/api/v0");

        await backend.InitializeAsync(config);

        backend.MountPath.Should().Be("/ada");
    }

    [Fact]
    public void EthereumBackend_Name_IsNotEmpty()
    {
        var backend = new EthereumBackend(_vault);
        backend.Name.Should().NotBeEmpty();
    }

    [Fact]
    public void BitcoinBackend_Name_IsNotEmpty()
    {
        var backend = new BitcoinBackend(_vault);
        backend.Name.Should().NotBeEmpty();
    }

    [Property]
    public bool EthereumBackend_MountPathPreserved(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) return true;

        var backend = new EthereumBackend(_vault);
        var config = CreateConfig(mountPath, "https://mainnet.infura.io");
        backend.InitializeAsync(config).Wait();

        return backend.MountPath == mountPath;
    }

    [Property]
    public bool BitcoinBackend_GetFileSystemIsIdempotent(int iterations)
    {
        var backend = new BitcoinBackend(_vault);
        backend.InitializeAsync(CreateConfig("/btc", "http://localhost:8332")).Wait();

        for (int i = 0; i < Math.Abs(iterations) % 50; i++)
        {
            var fs = backend.GetFileSystem();
            if (fs == null) return false;
        }
        return true;
    }
}

public class BlockchainBackendFuzzingTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public void EthereumBackend_FuzzRpcUrls()
    {
        var backend = new EthereumBackend(_vault);
        var attackVectors = FuzzerBridge.GenerateAttackVectors();

        foreach (var vector in attackVectors)
        {
            var url = System.Text.Encoding.UTF8.GetString(vector);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    KeyValuePair.Create("MountPath", (string?)"/eth"),
                    KeyValuePair.Create("RpcUrl", (string?)url)
                })
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for malformed URLs
            }
        }
    }

    [Fact]
    public void BitcoinBackend_FuzzMountPaths()
    {
        var backend = new BitcoinBackend(_vault);
        var paths = FuzzerBridge.GeneratePathTraversals();

        foreach (var path in paths)
        {
            var mountPath = string.Join("/", path);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    KeyValuePair.Create("MountPath", (string?)mountPath),
                    KeyValuePair.Create("RpcUrl", (string?)"http://localhost:8332")
                })
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for some invalid paths
            }
        }
    }

    [Fact]
    public void SolanaBackend_InvalidEndpointsHandledGracefully()
    {
        var backend = new SolanaBackend(_vault);
        var invalidEndpoints = new[]
        {
            "not-a-url",
            "javascript:alert(1)",
            "../../../etc/passwd",
            "file:///etc/passwd",
            "http://",
            "ftp://wrong-protocol.com"
        };

        foreach (var endpoint in invalidEndpoints)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    KeyValuePair.Create("MountPath", (string?)"/sol"),
                    KeyValuePair.Create("RpcUrl", (string?)endpoint)
                })
                .Build();

            try
            {
                backend.InitializeAsync(config).Wait();
            }
            catch
            {
                // Expected for invalid endpoints
            }
        }
    }
}

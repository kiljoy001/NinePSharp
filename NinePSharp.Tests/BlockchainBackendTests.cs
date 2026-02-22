using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
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
    public async Task SolanaBackend_Initialization_Works()
    {
        var backend = new SolanaBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Server:Solana:MountPath", "/sol"),
            new KeyValuePair<string, string?>("Server:Solana:RpcUrl", "https://api.devnet.solana.com")
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
            new KeyValuePair<string, string?>("Server:Stellar:MountPath", "/stellar"),
            new KeyValuePair<string, string?>("Server:Stellar:HorizonUrl", "https://horizon-testnet.stellar.org"),
            new KeyValuePair<string, string?>("Server:Stellar:UsePublicNetwork", "false")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Stellar", backend.Name);
        Assert.Equal("/stellar", backend.MountPath);
        Assert.NotNull(backend.GetFileSystem());
    }

    [Fact]
    public async Task EthereumBackend_Initialization_Works_From_ServerSection()
    {
        var backend = new EthereumBackend(_vault);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Server:Ethereum:MountPath", "/eth"),
            new KeyValuePair<string, string?>("Server:Ethereum:RpcUrl", "https://ethereum-sepolia-rpc.publicnode.com")
        }).Build();

        await backend.InitializeAsync(config);
        Assert.Equal("Ethereum", backend.Name);
        Assert.Equal("/eth", backend.MountPath);
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

    [Fact]
    public async Task BitcoinFileSystem_Mock_Send_Updates_Balance_And_Transactions()
    {
        var password = $"btc-test-{Guid.NewGuid():N}";
        var fs = new BitcoinFileSystem(new BitcoinBackendConfig { MountPath = "/btc", Network = "Main" }, null, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var beforeBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("mock-btc-address:0.25000000")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("btc-mock-", txLog);
        Assert.Contains("amount=0.25", txLog);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "balance" }));
        var afterBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));
        Assert.True(afterBalance < beforeBalance);
    }

    [Fact]
    public async Task BitcoinFileSystem_Status_Reports_Live_And_Mock_Modes()
    {
        var mockFs = new BitcoinFileSystem(
            new BitcoinBackendConfig { MountPath = "/btc", Network = "Main" },
            null,
            _vault);

        await mockFs.WalkAsync(new Twalk(1, 1, 2, new[] { "status" }));
        var mockStatus = Encoding.UTF8.GetString((await mockFs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("Mode: Mock", mockStatus);

        var rpc = new NBitcoin.RPC.RPCClient(
            NBitcoin.RPC.RPCCredentialString.Parse("test:test"),
            "http://127.0.0.1:18443",
            NBitcoin.Network.RegTest);

        var liveFs = new BitcoinFileSystem(
            new BitcoinBackendConfig
            {
                MountPath = "/btc",
                Network = "RegTest",
                RpcUrl = "http://127.0.0.1:18443"
            },
            rpc,
            _vault);

        await liveFs.WalkAsync(new Twalk(1, 1, 2, new[] { "status" }));
        var liveStatus = Encoding.UTF8.GetString((await liveFs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("Mode: Live", liveStatus);
        Assert.Contains("Address: None", liveStatus);
    }

    [Fact]
    public async Task SolanaFileSystem_Mock_Send_Updates_Balance_And_Transactions()
    {
        var password = $"sol-test-{Guid.NewGuid():N}";
        var fs = new SolanaFileSystem(new SolanaBackendConfig { MountPath = "/sol" }, null, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var beforeBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("mock-sol-address:1.5")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("sol-mock-", txLog);
        Assert.Contains("amount=1.5", txLog);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "balance" }));
        var afterBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));
        Assert.True(afterBalance < beforeBalance);
    }

    [Fact]
    public async Task StellarFileSystem_Mock_Send_Updates_Balance_And_Transactions()
    {
        var password = $"xlm-test-{Guid.NewGuid():N}";
        var fs = new StellarFileSystem(new StellarBackendConfig { MountPath = "/stellar", HorizonUrl = "https://horizon.stellar.org" }, null, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var beforeBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("mock-xlm-address:3.75")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("xlm-mock-", txLog);
        Assert.Contains("amount=3.75", txLog);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "balance" }));
        var afterBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));
        Assert.True(afterBalance < beforeBalance);
    }

    [Fact]
    public async Task CardanoFileSystem_Mock_Send_Updates_Balance_And_Transactions()
    {
        var password = $"ada-test-{Guid.NewGuid():N}";
        var fs = new CardanoFileSystem(new CardanoBackendConfig { MountPath = "/cardano" }, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var beforeBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("mock-ada-address:5.0")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());
        Assert.Contains("ada-mock-", txLog);
        Assert.Contains("amount=5", txLog);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "balance" }));
        var afterBalance = ParseLeadingDecimal(Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()));
        Assert.True(afterBalance < beforeBalance);
    }

    [Fact]
    public async Task CardanoFileSystem_Unlock_Derives_Real_Address_Format()
    {
        var password = $"ada-derive-{Guid.NewGuid():N}";
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig { MountPath = "/cardano", Network = "Preprod" },
            _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "address" }));
        var address = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()).Trim();

        Assert.StartsWith("addr_test1", address, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CardanoFileSystem_Status_Uses_Blockfrost_When_Configured()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.True(request.Headers.TryGetValues("project_id", out var values));
            Assert.Contains("test-project", values);
            Assert.EndsWith("/blocks/latest", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"hash":"abc123"}""", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            httpClient);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "status" }));
        var content = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.Contains("Mode: Live (Blockfrost)", content);
        Assert.Contains("LatestBlock: abc123", content);
        Assert.Contains("API: https://cardano-preprod.blockfrost.io/api/v0", content);
    }

    [Fact]
    public async Task CardanoFileSystem_Balance_Uses_Blockfrost_When_Unlocked()
    {
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.True(request.Headers.TryGetValues("project_id", out var values));
            Assert.Contains("test-project", values);
            Assert.Contains("/addresses/", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"amount":[{"unit":"lovelace","quantity":"123456789"},{"unit":"policy.asset","quantity":"2"}]}""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            httpClient);

        var password = $"ada-live-test-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var content = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.Contains("123.456789 ADA (Live)", content);
    }

    [Fact]
    public async Task CardanoFileSystem_Balance_Falls_Back_To_Mock_When_Blockfrost_Fails()
    {
        var handler = new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        using var httpClient = new HttpClient(handler);
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            httpClient);

        var password = $"ada-live-fallback-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "balance" }));
        var content = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.Contains("ADA (Mock)", content);
    }

    [Fact]
    public async Task CardanoFileSystem_Send_LiveMode_Submits_Signed_Cbor()
    {
        var seenSubmit = false;
        var handler = new TestHttpMessageHandler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/tx/submit", StringComparison.Ordinal))
            {
                seenSubmit = true;
                Assert.True(request.Headers.TryGetValues("project_id", out var values));
                Assert.Contains("test-project", values);
                Assert.Equal("application/cbor", request.Content?.Headers.ContentType?.MediaType);

                var bytes = await request.Content!.ReadAsByteArrayAsync();
                Assert.Equal(new byte[] { 0xA1, 0x00 }, bytes);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("\"live_tx_hash_123\"")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            httpClient);

        var password = $"ada-live-send-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("cbor:a100")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.True(seenSubmit);
        Assert.Contains("live_tx_hash_123", txLog);
        Assert.Contains("status=submitted", txLog);
    }

    [Fact]
    public async Task CardanoFileSystem_Send_LiveMode_Rejects_Invalid_Format()
    {
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            new HttpClient(new TestHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        var password = $"ada-live-invalid-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" }));

        var ex = await Assert.ThrowsAsync<NinePProtocolException>(
            async () => await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes("not-a-command"))));

        Assert.Contains("address:amount", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CardanoFileSystem_Send_LiveMode_AddressAmount_Builds_And_Submits()
    {
        var handler = new TestHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/utxos", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"tx_hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","tx_index":0,"amount":[{"unit":"lovelace","quantity":"3000000"}]}]""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/blocks/latest", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"slot":123456,"hash":"blockhash"}""", Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/tx/submit", StringComparison.Ordinal))
            {
                Assert.Equal("application/cbor", request.Content?.Headers.ContentType?.MediaType);
                var payload = await request.Content!.ReadAsByteArrayAsync();
                Assert.NotEmpty(payload);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("\"built_tx_hash_456\"")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var fs = new CardanoFileSystem(
            new CardanoBackendConfig
            {
                MountPath = "/cardano",
                Network = "Preprod",
                BlockfrostProjectId = "test-project",
                BlockfrostApiUrl = "https://cardano-preprod.blockfrost.io/api/v0"
            },
            _vault,
            httpClient);

        var password = $"ada-live-build-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "address" }));
        var sender = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()).Trim();

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes($"{sender}:1.0")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.Contains("built_tx_hash_456", txLog);
        Assert.Contains("status=submitted", txLog);
    }

    [Fact]
    public async Task SolanaFileSystem_Send_LiveMode_Uses_RpcClient()
    {
        var latest = new Solnet.Rpc.Core.Http.RequestResult<Solnet.Rpc.Messages.ResponseValue<Solnet.Rpc.Models.LatestBlockHash>>
        {
            WasHttpRequestSuccessful = true,
            WasRequestSuccessfullyHandled = true,
            Result = new Solnet.Rpc.Messages.ResponseValue<Solnet.Rpc.Models.LatestBlockHash>
            {
                Value = new Solnet.Rpc.Models.LatestBlockHash { Blockhash = "9xQeWvG816bUx9EPf1S6nFhB4m5xBvXkY9n8k8fYwYv", LastValidBlockHeight = 42 }
            }
        };

        var submitted = new Solnet.Rpc.Core.Http.RequestResult<string>
        {
            WasHttpRequestSuccessful = true,
            WasRequestSuccessfullyHandled = true,
            Result = "solana_live_sig_123"
        };

        var rpc = new Mock<Solnet.Rpc.IRpcClient>();
        rpc.Setup(r => r.GetLatestBlockHashAsync(Solnet.Rpc.Types.Commitment.Finalized))
            .ReturnsAsync(latest);
        rpc.Setup(r => r.SendTransactionAsync(
                It.IsAny<byte[]>(),
                false,
                Solnet.Rpc.Types.Commitment.Confirmed))
            .ReturnsAsync(submitted);

        var fs = new SolanaFileSystem(new SolanaBackendConfig { MountPath = "/sol" }, rpc.Object, _vault);

        var password = $"sol-live-send-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "address" }));
        var destination = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()).Trim();
        Assert.Matches("^[1-9A-HJ-NP-Za-km-z]+$", destination);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes($"{destination}:0.01")));
        rpc.Verify(r => r.SendTransactionAsync(It.IsAny<byte[]>(), false, Solnet.Rpc.Types.Commitment.Confirmed), Times.Once);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.Contains("solana_live_sig_123", txLog);
    }

    [Fact]
    public async Task StellarFileSystem_Send_LiveMode_Uses_Horizon_HttpClient()
    {
        var source = StellarDotnetSdk.Accounts.KeyPair.Random();
        var destination = StellarDotnetSdk.Accounts.KeyPair.Random();
        Assert.False(string.IsNullOrWhiteSpace(source.SecretSeed));

        var sawSubmit = false;
        var accountLookups = 0;

        var handler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.StartsWith("/accounts/", StringComparison.Ordinal))
            {
                accountLookups++;
                var accountId = path.Substring("/accounts/".Length);
                var payload =
                    $$"""
                    {
                      "account_id": "{{accountId}}",
                      "sequence": "123456",
                      "subentry_count": 0,
                      "balances": [
                        { "balance": "50.0000000", "asset_type": "native" }
                      ],
                      "thresholds": { "low_threshold": 1, "med_threshold": 1, "high_threshold": 1 },
                      "flags": { "auth_required": false, "auth_revocable": false, "auth_immutable": false, "auth_clawback_enabled": false },
                      "signers": [
                        { "weight": 1, "key": "{{accountId}}", "type": "ed25519_public_key" }
                      ],
                      "data": {},
                      "_links": {
                        "self": { "href": "https://horizon-testnet.stellar.org/accounts/{{accountId}}" }
                      }
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && path == "/transactions")
            {
                sawSubmit = true;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("tx=", body, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "hash": "stellar_live_hash_abc",
                          "ledger": 123,
                          "result_xdr": "AAAAAgAAAAAAAAA",
                          "result_meta_xdr": "AAAAAgAAAAAAAAA",
                          "envelope_xdr": "AAAAAgAAAAAAAAA"
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://horizon-testnet.stellar.org") };
        var server = new StellarDotnetSdk.Server("https://horizon-testnet.stellar.org", httpClient);
        var fs = new StellarFileSystem(
            new StellarBackendConfig
            {
                MountPath = "/stellar",
                HorizonUrl = "https://horizon-testnet.stellar.org",
                UsePublicNetwork = false
            },
            server,
            _vault);

        var password = $"xlm-live-send-{Guid.NewGuid():N}";
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "import" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes($"{password}:{source.SecretSeed}")));
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes($"{destination.AccountId}:1.25")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "transactions" }));
        var txLog = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray());

        Assert.True(sawSubmit);
        Assert.True(accountLookups >= 2);
        Assert.Contains("stellar_live_hash_abc", txLog);
        Assert.Contains("status=submitted", txLog);
    }

    [Fact]
    public async Task SolanaFileSystem_Import_PrivateKeyOnly_Derives_PublicAddress()
    {
        var wallet = new Solnet.Wallet.Wallet(new Solnet.Wallet.Bip39.Mnemonic(Solnet.Wallet.Bip39.WordList.English, Solnet.Wallet.Bip39.WordCount.Twelve));
        var account = wallet.GetAccount(0);
        var password = $"sol-import-{Guid.NewGuid():N}";
        var fs = new SolanaFileSystem(new SolanaBackendConfig { MountPath = "/sol" }, null, _vault);

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "import" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes($"{password}:{account.PrivateKey.Key}")));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "address" }));
        var address = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()).Trim();

        Assert.Equal(account.PublicKey.Key, address);
        Assert.DoesNotContain("sol_mock_", address, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolanaFileSystem_Unlock_LegacyPrivateOnlyVault_Derives_PublicAddress()
    {
        var wallet = new Solnet.Wallet.Wallet(new Solnet.Wallet.Bip39.Mnemonic(Solnet.Wallet.Bip39.WordList.English, Solnet.Wallet.Bip39.WordCount.Twelve));
        var account = wallet.GetAccount(0);
        var passwordText = $"sol-legacy-{Guid.NewGuid():N}";
        using var password = ToSecureString(passwordText);

        byte[] idSalt = Encoding.UTF8.GetBytes("Solana_Vault_ID_Salt_v1");
        var seed = _vault.DeriveSeed(password, idSalt);
        var hiddenId = _vault.GenerateHiddenId(seed);

        byte[] privateKeyBytes = Encoding.UTF8.GetBytes(account.PrivateKey.Key);
        try
        {
            var ciphertext = _vault.Encrypt(privateKeyBytes, password);
            File.WriteAllBytes(_vault.GetVaultPath($"sol_vault_{hiddenId}.vlt"), ciphertext);
        }
        finally
        {
            Array.Clear(privateKeyBytes);
        }

        var fs = new SolanaFileSystem(new SolanaBackendConfig { MountPath = "/sol" }, null, _vault);
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "unlock" }));
        await fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(passwordText)));

        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "address" }));
        var address = Encoding.UTF8.GetString((await fs.ReadAsync(new Tread(1, 2, 0, 4096))).Data.ToArray()).Trim();

        Assert.Equal(account.PublicKey.Key, address);
        Assert.DoesNotContain("sol_mock_", address, StringComparison.Ordinal);
    }

    private static decimal ParseLeadingDecimal(string content)
    {
        var token = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).First();
        return decimal.Parse(token, NumberStyles.Number, CultureInfo.InvariantCulture);
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

    [Property(MaxTest = 20)]
    public bool TransferChains_SendPayload_Robustness_Property(int chainType, byte[] payload)
    {
        if (payload == null || payload.Length == 0 || payload.Length > 4096)
        {
            return true;
        }

        var normalized = Math.Abs(chainType % 4);
        INinePFileSystem fs = normalized switch
        {
            0 => new BitcoinFileSystem(new BitcoinBackendConfig(), null, _vault),
            1 => new SolanaFileSystem(new SolanaBackendConfig(), null, _vault),
            2 => new StellarFileSystem(new StellarBackendConfig(), null, _vault),
            _ => new CardanoFileSystem(new CardanoBackendConfig(), _vault)
        };

        var password = $"prop-transfer-{normalized}-{Guid.NewGuid():N}";

        try
        {
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "wallets", "create" })).GetAwaiter().GetResult();
            fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password))).GetAwaiter().GetResult();
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "unlock" })).GetAwaiter().GetResult();
            fs.WriteAsync(new Twrite(1, 2, 0, Encoding.UTF8.GetBytes(password))).GetAwaiter().GetResult();
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "..", "send" })).GetAwaiter().GetResult();

            try
            {
                fs.WriteAsync(new Twrite(1, 2, 0, payload)).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (IsExpectedTransferException(ex))
            {
                // Expected parser/validation failures are acceptable outcomes.
            }

            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "..", "status" })).GetAwaiter().GetResult();
            fs.ReadAsync(new Tread(1, 2, 0, 2048)).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    private static bool IsExpectedTransferException(Exception ex)
    {
        var candidate = ex is AggregateException agg && agg.InnerException != null
            ? agg.InnerException
            : ex;

        return candidate is NinePProtocolException
               || candidate is InvalidOperationException
               || candidate is ArgumentException
               || candidate is FormatException;
    }

    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var c in value)
        {
            secure.AppendChar(c);
        }

        secure.MakeReadOnly();
        return secure;
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}

using NinePSharp.Constants;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace NinePSharp.Tests.Integration;

public class BlockchainLiveNetworkTests
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static bool IsLiveEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable("NINEPSHARP_RUN_LIVE_BLOCKCHAIN_TESTS");
        return string.Equals(enabled, "1", StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Ethereum_Sepolia_BlockNumber_Is_Reachable()
    {
        if (!IsLiveEnabled()) return;

        var url = Environment.GetEnvironmentVariable("NINEPSHARP_ETH_RPC_URL")
                  ?? "https://ethereum-sepolia-rpc.publicnode.com";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"eth_blockNumber","params":[]}""",
                Encoding.UTF8,
                "application/json")
        };

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result").GetString();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.StartsWith("0x", result!, StringComparison.OrdinalIgnoreCase);
        Assert.True(Convert.ToInt64(result, 16) > 0);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Solana_Devnet_GetSlot_Is_Reachable()
    {
        if (!IsLiveEnabled()) return;

        var url = Environment.GetEnvironmentVariable("NINEPSHARP_SOL_RPC_URL")
                  ?? "https://api.devnet.solana.com";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"getSlot","params":[]}""",
                Encoding.UTF8,
                "application/json")
        };

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var slot = doc.RootElement.GetProperty("result").GetInt64();
        Assert.True(slot > 0);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Stellar_Testnet_Horizon_Is_Reachable()
    {
        if (!IsLiveEnabled()) return;

        var url = Environment.GetEnvironmentVariable("NINEPSHARP_STELLAR_HORIZON_URL")
                  ?? "https://horizon-testnet.stellar.org";

        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var passphrase = doc.RootElement.GetProperty("network_passphrase").GetString();

        Assert.False(string.IsNullOrWhiteSpace(passphrase));
        Assert.Contains("Test SDF Network", passphrase!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Bitcoin_TestnetRpc_GetBlockchainInfo_When_Configured()
    {
        if (!IsLiveEnabled()) return;

        var url = Environment.GetEnvironmentVariable("NINEPSHARP_BTC_RPC_URL");
        var user = Environment.GetEnvironmentVariable("NINEPSHARP_BTC_RPC_USER");
        var pass = Environment.GetEnvironmentVariable("NINEPSHARP_BTC_RPC_PASSWORD");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                """{"jsonrpc":"1.0","id":"ninepsharp-live","method":"getblockchaininfo","params":[]}""",
                Encoding.UTF8,
                "application/json")
        };

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var chain = doc.RootElement.GetProperty("result").GetProperty("chain").GetString();

        Assert.False(string.IsNullOrWhiteSpace(chain));
        Assert.NotEqual("main", chain);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Cardano_Blockfrost_BlocksLatest_When_Configured()
    {
        if (!IsLiveEnabled()) return;

        var projectId = Environment.GetEnvironmentVariable("NINEPSHARP_CARDANO_BLOCKFROST_PROJECT_ID");
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var apiUrl = Environment.GetEnvironmentVariable("NINEPSHARP_CARDANO_BLOCKFROST_URL")
                     ?? "https://cardano-preprod.blockfrost.io/api/v0";
        var endpoint = $"{apiUrl.TrimEnd('/')}/blocks/latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Add("project_id", projectId);

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var hash = doc.RootElement.GetProperty("hash").GetString();

        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Cardano_Blockfrost_AddressUtxos_When_Configured()
    {
        if (!IsLiveEnabled()) return;

        var projectId = Environment.GetEnvironmentVariable("NINEPSHARP_CARDANO_BLOCKFROST_PROJECT_ID");
        var address = Environment.GetEnvironmentVariable("NINEPSHARP_CARDANO_ADDRESS");
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var apiUrl = Environment.GetEnvironmentVariable("NINEPSHARP_CARDANO_BLOCKFROST_URL")
                     ?? "https://cardano-preprod.blockfrost.io/api/v0";
        var endpoint = $"{apiUrl.TrimEnd('/')}/addresses/{Uri.EscapeDataString(address)}/utxos?count=1&page=1&order=desc";

        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Add("project_id", projectId);

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Solana_GetBalance_When_Address_Configured()
    {
        if (!IsLiveEnabled()) return;

        var address = Environment.GetEnvironmentVariable("NINEPSHARP_SOL_ADDRESS");
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var url = Environment.GetEnvironmentVariable("NINEPSHARP_SOL_RPC_URL")
                  ?? "https://api.devnet.solana.com";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                $$"""{"jsonrpc":"2.0","id":1,"method":"getBalance","params":["{{address}}"]}""",
                Encoding.UTF8,
                "application/json")
        };

        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var value = doc.RootElement.GetProperty("result").GetProperty("value").GetInt64();
        Assert.True(value >= 0);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Stellar_Account_When_Address_Configured()
    {
        if (!IsLiveEnabled()) return;

        var address = Environment.GetEnvironmentVariable("NINEPSHARP_STELLAR_ADDRESS");
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var horizon = Environment.GetEnvironmentVariable("NINEPSHARP_STELLAR_HORIZON_URL")
                      ?? "https://horizon-testnet.stellar.org";
        var endpoint = $"{horizon.TrimEnd('/')}/accounts/{Uri.EscapeDataString(address)}";

        using var resp = await Http.GetAsync(endpoint);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var accountId = doc.RootElement.GetProperty("account_id").GetString();
        Assert.Equal(address, accountId);
    }
}

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
    public async Task BitcoinFileSystem_Walk_To_Balance_Works()
    {
        var config = new BitcoinBackendConfig { MountPath = "/btc", Network = "Main" };
        var fs = new BitcoinFileSystem(config, null, _vault);

        var twalk = new Twalk(1, 1, 2, new[] { "balance" });
        var response = await fs.WalkAsync(twalk);

        Assert.Single(response.Wqid);
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
}

using System.Text;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class ConfigSecretResolverTests
{
    [Fact]
    public void ConfigSecretResolver_Resolves_Recursive_Secrets()
    {
        byte[] masterKey = Encoding.UTF8.GetBytes("master_key");
        string secretValue = "real_api_key";
        string protectedSecret = LuxVault.ProtectConfig(Encoding.UTF8.GetBytes(secretValue), masterKey);

        var config = new TestConfig
        {
            Nested = new TestNestedConfig
            {
                Url = "http://localhost:8545",
                Token = protectedSecret
            },
            RootPath = protectedSecret
        };

        ConfigSecretResolver.ResolveSecrets(config, masterKey);

        Assert.Equal(secretValue, config.Nested!.Token);
        Assert.Equal(secretValue, config.RootPath);
        Assert.Equal("http://localhost:8545", config.Nested.Url);
    }

    [Fact]
    public void ConfigSecretResolver_Handles_Normal_Strings()
    {
        byte[] masterKey = Encoding.UTF8.GetBytes("master_key");
        var config = new TestNestedConfig { Url = "http://normal.com" };

        ConfigSecretResolver.ResolveSecrets(config, masterKey);

        Assert.Equal("http://normal.com", config.Url);
    }

    private sealed class TestConfig
    {
        public TestNestedConfig? Nested { get; set; }
        public string RootPath { get; set; } = string.Empty;
    }

    private sealed class TestNestedConfig
    {
        public string Url { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
    }
}

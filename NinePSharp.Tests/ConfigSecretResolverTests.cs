using NinePSharp.Constants;
using System.Collections.Generic;
using System.Text;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class ConfigSecretResolverTests
    {
        [Fact]
        public void ConfigSecretResolver_Resolves_Recursive_Secrets()
        {
            byte[] masterKey = Encoding.UTF8.GetBytes("master_key");
            var secretValue = "real_api_key";
            var protectedSecret = LuxVault.ProtectConfig(Encoding.UTF8.GetBytes(secretValue), masterKey);

            var config = new ServerConfig
            {
                Ethereum = new EthereumBackendConfig
                {
                    RpcUrl = "http://localhost:8545",
                    DefaultAccount = protectedSecret // Secret in Ethereum config
                },
                Secret = new SecretBackendConfig
                {
                    RootPath = protectedSecret // Secret in Secret config
                }
            };

            ConfigSecretResolver.ResolveSecrets(config, masterKey);

            Assert.Equal(secretValue, config.Ethereum.DefaultAccount);
            Assert.Equal(secretValue, config.Secret.RootPath);
            Assert.Equal("http://localhost:8545", config.Ethereum.RpcUrl); // Normal string unchanged
        }

        [Fact]
        public void ConfigSecretResolver_Handles_Normal_Strings()
        {
            byte[] masterKey = Encoding.UTF8.GetBytes("master_key");
            var config = new EthereumBackendConfig { RpcUrl = "http://normal.com" };

            ConfigSecretResolver.ResolveSecrets(config, masterKey);

            Assert.Equal("http://normal.com", config.RpcUrl);
        }
    }
}

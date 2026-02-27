using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Backends.Blockchain.Tests;

public class BlockchainPropertyFuzzTests
{
    private static readonly ILuxVaultService Vault = new LuxVaultService();

    [Property(MaxTest = 45)]
    public bool AllChains_Walk_DotDot_Has_Bounded_Qids_Property(string[]? segments)
    {
        string[] path = (segments ?? Array.Empty<string>())
            .Take(6)
            .Select(s => string.IsNullOrWhiteSpace(s) ? ".." : s.Trim())
            .ToArray();

        foreach (var (name, factory) in BuildFactories())
        {
            var fs = factory();
            var walk = fs.WalkAsync(new Twalk(1, 1, 1, path)).GetAwaiter().GetResult();
            if (walk.Wqid.Length > path.Length)
            {
                return false;
            }

            var unwindPath = Enumerable.Repeat("..", Math.Max(1, path.Length)).ToArray();
            var unwind = fs.WalkAsync(new Twalk(2, 1, 1, unwindPath)).GetAwaiter().GetResult();
            if (unwind.Wqid.Length > unwindPath.Length)
            {
                return false;
            }
        }

        return true;
    }

    [Property(MaxTest = 40)]
    public bool AllChains_Root_Readdir_Pagination_RoundTrip_Property(PositiveInt pageSeed)
    {
        uint pageSize = (uint)Math.Clamp(pageSeed.Get % 512 + 24, 24, 512);

        foreach (var (_, factory) in BuildFactories())
        {
            var fs = factory();
            var full = fs.ReadAsync(new Tread(10, 1, 0, ushort.MaxValue)).GetAwaiter().GetResult();
            byte[] fullBytes = full.Data.ToArray();

            var reconstructed = new List<byte>();
            ulong offset = 0;
            int guard = 0;
            while (guard++ < 1024)
            {
                var page = fs.ReadAsync(new Tread(11, 1, offset, pageSize)).GetAwaiter().GetResult();
                byte[] chunk = page.Data.ToArray();
                if (chunk.Length == 0)
                {
                    break;
                }

                reconstructed.AddRange(chunk);
                offset += (ulong)chunk.Length;
            }

            if (!reconstructed.SequenceEqual(fullBytes))
            {
                return false;
            }
        }

        return true;
    }

    [Property(MaxTest = 35)]
    public bool Blockchain_Clone_PathState_Isolation_Property(string accountSeed)
    {
        string account = string.IsNullOrWhiteSpace(accountSeed)
            ? "acct"
            : new string(accountSeed.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').Take(32).ToArray());
        if (string.IsNullOrWhiteSpace(account))
        {
            account = "acct";
        }

        foreach (var (_, factory) in BuildFactories())
        {
            var root = factory();
            var writer = root.Clone();
            var reader = root.Clone();

            string backendAccount = root is EthereumFileSystem
                ? "0x" + new string('a', 40)
                : account;

            writer.WalkAsync(new Twalk(20, 1, 1, new[] { "wallets", "use" })).GetAwaiter().GetResult();
            try
            {
                writer.WriteAsync(new Twrite(21, 1, 0, Encoding.UTF8.GetBytes(backendAccount))).GetAwaiter().GetResult();
            }
            catch (NinePProtocolException)
            {
                continue;
            }

            var status = writer.Clone();
            status.WalkAsync(new Twalk(22, 1, 1, new[] { "..", "status" })).GetAwaiter().GetResult();
            _ = status.ReadAsync(new Tread(23, 1, 0, 4096)).GetAwaiter().GetResult();

            var rootRead = reader.ReadAsync(new Tread(24, 1, 0, 4096)).GetAwaiter().GetResult();
            if (rootRead.Data.Length == 0)
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public async Task Fuzz_Transfer_Writes_Do_Not_Throw_Unexpected_Runtime_Exceptions()
    {
        var random = new Random(0xB10C);
        var transferFileSystems = BuildTransferFactories();

        for (int i = 0; i < 260; i++)
        {
            foreach (var (name, factory) in transferFileSystems)
            {
                var fs = factory();
                await fs.WalkAsync(new Twalk((ushort)(100 + i), 1, 1, new[] { "send" }));

                int len = random.Next(0, 96);
                byte[] payload = new byte[len];
                random.NextBytes(payload);

                try
                {
                    _ = await fs.WriteAsync(new Twrite((ushort)(110 + i), 1, 0, payload));
                }
                catch (Exception ex)
                {
                    Assert.True(
                        ex is NinePProtocolException || ex is InvalidOperationException,
                        $"Unexpected exception for {name} transfer fuzz iteration {i}: {ex.GetType().Name}");
                }
            }
        }
    }

    [Fact]
    public async Task Fuzz_Ethereum_Path_And_Call_Grammar_Does_Not_Throw_Unexpected_Runtime_Exceptions()
    {
        var random = new Random(0xE7A);

        for (int i = 0; i < 220; i++)
        {
            var fs = BuildEthereumFileSystem();
            try
            {
                switch (random.Next(0, 5))
                {
                    case 0:
                    {
                        await fs.WalkAsync(new Twalk((ushort)(200 + i), 1, 1, RandomEthereumPath(random)));
                        break;
                    }
                    case 1:
                    {
                        var wallet = fs.Clone();
                        await wallet.WalkAsync(new Twalk((ushort)(220 + i), 1, 1, new[] { "wallets", "use" }));
                        byte[] maybeAddress = Encoding.UTF8.GetBytes(random.Next(0, 2) == 0
                            ? "0x" + (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..40]
                            : Guid.NewGuid().ToString("N"));
                        await wallet.WriteAsync(new Twrite((ushort)(221 + i), 1, 0, maybeAddress));
                        break;
                    }
                    case 2:
                    {
                        var call = fs.Clone();
                        await call.WalkAsync(new Twalk((ushort)(230 + i), 1, 1, new[] { "contracts", "0xabc", "call" }));
                        byte[] payload = new byte[random.Next(0, 80)];
                        random.NextBytes(payload);
                        await call.WriteAsync(new Twrite((ushort)(231 + i), 1, 0, payload));
                        break;
                    }
                    case 3:
                    {
                        await fs.ReadAsync(new Tread((ushort)(240 + i), 1, (ulong)random.Next(0, 128), (uint)random.Next(1, 512)));
                        break;
                    }
                    default:
                    {
                        await fs.StatAsync(new Tstat((ushort)(250 + i), 1));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.True(
                    ex is NinePProtocolException || ex is InvalidOperationException || ex is ArgumentOutOfRangeException,
                    $"Unexpected Ethereum fuzz exception at iteration {i}: {ex.GetType().Name}");
            }
        }
    }

    private static string[] RandomEthereumPath(Random random)
    {
        string[] atoms =
        {
            "..",
            ".",
            "wallets",
            "contracts",
            "status",
            "balance",
            "use",
            "abi",
            "call",
            $"0x{Guid.NewGuid():N}"[..10]
        };

        int length = random.Next(0, 5);
        var path = new string[length];
        for (int i = 0; i < length; i++)
        {
            path[i] = atoms[random.Next(0, atoms.Length)];
        }

        return path;
    }

    private static IEnumerable<(string Name, Func<INinePFileSystem> Factory)> BuildFactories()
    {
        yield return ("bitcoin", BuildBitcoinFileSystem);
        yield return ("solana", BuildSolanaFileSystem);
        yield return ("stellar", BuildStellarFileSystem);
        yield return ("cardano", BuildCardanoFileSystem);
        yield return ("ethereum", BuildEthereumFileSystem);
    }

    private static IEnumerable<(string Name, Func<INinePFileSystem> Factory)> BuildTransferFactories()
    {
        yield return ("bitcoin", BuildBitcoinFileSystem);
        yield return ("solana", BuildSolanaFileSystem);
        yield return ("cardano", BuildCardanoFileSystem);
        yield return ("stellar", BuildStellarFileSystem);
    }

    private static BitcoinFileSystem BuildBitcoinFileSystem()
    {
        return new BitcoinFileSystem(
            new BitcoinBackendConfig { Network = "Main", AllowedMethods = new List<string>() },
            rpcClient: null,
            vault: Vault);
    }

    private static SolanaFileSystem BuildSolanaFileSystem()
    {
        return new SolanaFileSystem(
            new SolanaBackendConfig { RpcUrl = "http://localhost", AllowedMethods = new List<string>() },
            rpcClient: null,
            vault: Vault);
    }

    private static StellarFileSystem BuildStellarFileSystem()
    {
        return new StellarFileSystem(
            new StellarBackendConfig { HorizonUrl = "http://localhost", UsePublicNetwork = false, AllowedMethods = new List<string>() },
            rpcClient: null,
            vault: Vault);
    }

    private static CardanoFileSystem BuildCardanoFileSystem()
    {
        return new CardanoFileSystem(
            new CardanoBackendConfig { Network = "preview", AllowedMethods = new List<string>() },
            vault: Vault);
    }

    private static EthereumFileSystem BuildEthereumFileSystem()
    {
        var rpc = new JsonRpcClient(new HttpClient(new StaticJsonRpcHandler()), "http://localhost");
        return new EthereumFileSystem(
            new EthereumBackendConfig
            {
                RpcUrl = "http://localhost",
                DefaultAccount = "0x0000000000000000000000000000000000000000",
                AllowedMethods = new List<string>()
            },
            rpc,
            Vault);
    }

    private sealed class StaticJsonRpcHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"0x0\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

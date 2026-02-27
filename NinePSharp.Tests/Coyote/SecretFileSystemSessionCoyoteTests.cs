using NinePSharp.Constants;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

[Collection("Sequential Secret Tests")]
public class SecretFileSystemSessionCoyoteTests
{
    private static int _keysInitialized;

    [Fact]
    public void Coyote_Concurrent_Unlocks_Same_Secret_Converge_Without_Crash()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(120)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            EnsureSessionKeys();
            SecretFileSystem.ClearSessionPasswords();

            ILuxVaultService vault = new LuxVaultService();
            var config = new SecretBackendConfig { RootPath = "secrets-coyote-tests" };

            string password = "pw-coyote-123";
            string name = "secret_" + Guid.NewGuid().ToString("N")[..12];
            string value = "value_" + Guid.NewGuid().ToString("N")[..12];

            await ProvisionAsync(vault, config, password, name, value);

            var fsA = new SecretFileSystem(NullLogger.Instance, config, vault);
            var fsB = new SecretFileSystem(NullLogger.Instance, config, vault);

            await fsA.WalkAsync(new Twalk(1, 1, 1, new[] { "unlock" }));
            await fsB.WalkAsync(new Twalk(2, 1, 1, new[] { "unlock" }));

            byte[] unlockPayload = Encoding.UTF8.GetBytes($"{password}:{name}");

            var t1 = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                return await fsA.WriteAsync(new Twrite(10, 1, 0, unlockPayload));
            });

            var t2 = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                return await fsB.WriteAsync(new Twrite(11, 1, 0, unlockPayload));
            });

            var writes = await CoyoteTask.WhenAll(t1, t2);
            if (writes.Any(w => w.Count != (uint)unlockPayload.Length))
            {
                throw new Exception("Unexpected unlock write count.");
            }

            var fsRead = new SecretFileSystem(NullLogger.Instance, config, vault);
            await fsRead.WalkAsync(new Twalk(3, 1, 1, new[] { "vault", name }));
            _ = await fsRead.ReadAsync(new Tread(4, 1, 0, 8192));

            var fsRemove = new SecretFileSystem(NullLogger.Instance, config, vault);
            await fsRemove.WalkAsync(new Twalk(5, 1, 1, new[] { "vault", name }));
            try
            {
                var removed = await fsRemove.RemoveAsync(new Tremove(6, 1));
                if (removed.Tag != 6)
                {
                    throw new Exception("Remove did not return expected tag.");
                }
            }
            catch (NinePNotSupportedException)
            {
                // Allowed: concurrent unlocks may race and leave no session entry to remove.
            }

            var fsAfter = new SecretFileSystem(NullLogger.Instance, config, vault);
            await fsAfter.WalkAsync(new Twalk(7, 1, 1, new[] { "vault", name }));
            _ = await fsAfter.ReadAsync(new Tread(8, 1, 0, 8192));
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    private static async Task ProvisionAsync(ILuxVaultService vault, SecretBackendConfig config, string password, string name, string value)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, config, vault);
        await fs.WalkAsync(new Twalk(1, 1, 1, new[] { "provision" }));

        byte[] payload = Encoding.UTF8.GetBytes($"{password}:{name}:{value}");
        try
        {
            var write = await fs.WriteAsync(new Twrite(1, 1, 0, payload));
            write.Count.Should().Be((uint)payload.Length);
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private static void EnsureSessionKeys()
    {
        if (Interlocked.Exchange(ref _keysInitialized, 1) == 1)
        {
            return;
        }

        byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        LuxVault.InitializeSessionKey(key);
        ProtectedSecret.InitializeSessionKey(key);
    }
}

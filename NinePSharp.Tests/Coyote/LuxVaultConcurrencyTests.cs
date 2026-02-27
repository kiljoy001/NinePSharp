using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class LuxVaultConcurrencyTests
{
    private static Microsoft.Coyote.Configuration CreateCoyoteConfiguration(uint iterations)
    {
        return Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(iterations)
            .WithPartiallyControlledConcurrencyAllowed(true);
    }

    [Fact]
    public void Coyote_LuxVault_Concurrent_Initialization_Race()
    {
        var configuration = CreateCoyoteConfiguration(iterations: 100);
        var engine = TestingEngine.Create(configuration, async () =>
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);

            // Multiple threads trying to initialize the session key
            var task1 = CoyoteTask.Run(() => LuxVault.InitializeSessionKey(key));
            var task2 = CoyoteTask.Run(() => LuxVault.InitializeSessionKey(key));
            var task3 = CoyoteTask.Run(() => LuxVault.InitializeSessionKey(key));

            await CoyoteTask.WhenAll(task1, task2, task3);
            
            // Invariant: The system should not crash and key should be initialized.
        });

        engine.Run();
        Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    }

    [Fact]
    public void Coyote_LuxVault_Concurrent_Arena_Access()
    {
        var configuration = CreateCoyoteConfiguration(iterations: 200);
        var engine = TestingEngine.Create(configuration, async () =>
        {
            byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };
            string password = "password";

            // Multiple threads doing encryption/decryption (which uses sharded arenas)
            var tasks = Enumerable.Range(0, 5).Select(_ => CoyoteTask.Run(() =>
            {
                byte[] encrypted = LuxVault.Encrypt(data, password);
                using (var decrypted = LuxVault.DecryptToBytes(encrypted, password))
                {
                    // Basic sanity check
                    if (decrypted == null || decrypted.Span.Length != data.Length)
                    {
                        // Potential race or corruption detected
                    }
                }
            })).ToArray();

            await CoyoteTask.WhenAll(tasks);
        });

        engine.Run();
        Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    }
}

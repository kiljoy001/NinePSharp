using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests.Coyote;

/// <summary>
/// Coyote-based concurrency tests for LuxVault mutation testing.
/// These tests systematically explore thread interleavings to find race conditions
/// in shared state that deterministic tests cannot detect.
/// </summary>
public class LuxVaultCoyoteMutationTests
{
    /// <summary>
    /// Tests InitializeSessionKey for TOCTOU race conditions.
    /// Kills mutants on lines 76-77 (null check removal, pinned flag).
    ///
    /// Scenario: Two threads both read _sessionKey == null, both try to allocate.
    /// Expected: Exactly one key is set, no memory leak, operations work correctly.
    /// </summary>
    [Fact]
    public void Coyote_InitializeSessionKey_Concurrent_Race_Condition()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(500)
            .WithMaxSchedulingSteps(1000);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            // Reset session key to test initialization race
            var sessionKeyField = typeof(LuxVault).GetField("_sessionKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            sessionKeyField.SetValue(null, null);

            byte[] key1 = new byte[32];
            byte[] key2 = new byte[32];
            RandomNumberGenerator.Fill(key1);
            RandomNumberGenerator.Fill(key2);

            // Two threads attempting concurrent initialization
            var task1 = Task.Run(() => LuxVault.InitializeSessionKey(key1));
            var task2 = Task.Run(() => LuxVault.InitializeSessionKey(key2));

            await Task.WhenAll(task1, task2);

            // Invariant 1: Session key must be initialized (not null)
            var finalKey = (byte[])sessionKeyField.GetValue(null);
            finalKey.Should().NotBeNull("Session key must be initialized after concurrent calls");

            // Invariant 2: Exactly one of the two keys was set (no corruption)
            (finalKey.SequenceEqual(key1) || finalKey.SequenceEqual(key2))
                .Should().BeTrue("Session key must match one of the initialization attempts");

            // Invariant 3: Encryption/decryption still works (no memory corruption)
            var plaintext = Encoding.UTF8.GetBytes("CoyoteTest");
            var encrypted = LuxVault.Encrypt(plaintext, "password");
            using var decrypted = LuxVault.DecryptToBytes(encrypted, "password");
            decrypted.Should().NotBeNull();
            decrypted.Span.ToArray().Should().Equal(plaintext);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0,
            $"Coyote found {engine.TestReport.NumOfFoundBugs} race conditions in InitializeSessionKey. " +
            $"This indicates the mutant removed the null check or changed pinning, causing double-initialization.");
    }

    /// <summary>
    /// Tests SecureMemoryArena for concurrent allocation/free races.
    /// Kills mutants on line 42 (Arena.Free removal) and related Array.Clear mutations.
    ///
    /// Scenario: 10 threads allocate concurrently. If Free is removed, arena exhausts.
    /// Expected: All allocations succeed, arena returns to baseline, no leaks.
    /// </summary>
    [Fact]
    public void Coyote_Arena_Concurrent_Allocations_Return_To_Baseline()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(100);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var arenaField = typeof(LuxVault).GetField("Arena",
                BindingFlags.NonPublic | BindingFlags.Static);
            var arenaInstance = arenaField.GetValue(null);
            var activeAllocationsProp = arenaInstance.GetType()
                .GetProperty("ActiveAllocations");

            int baseline = (int)activeAllocationsProp.GetValue(arenaInstance);

            // 10 concurrent encrypt/decrypt operations stressing the arena
            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                var plaintext = Encoding.UTF8.GetBytes($"Data{i}");
                var encrypted = LuxVault.Encrypt(plaintext, "password");
                using var decrypted = LuxVault.DecryptToBytes(encrypted, "password");

                // Verify correctness of each operation
                decrypted.Should().NotBeNull($"Decryption {i} failed under concurrency");
                decrypted.Span.ToArray().Should().Equal(plaintext,
                    $"Decrypted data {i} doesn't match plaintext - possible memory corruption");
            })).ToArray();

            await Task.WhenAll(tasks);

            // Invariant: Arena must return to baseline (no memory leaks)
            int final = (int)activeAllocationsProp.GetValue(arenaInstance);
            final.Should().Be(baseline,
                "Arena leaked memory under concurrent allocations. " +
                "This indicates the mutant removed Arena.Free() or Array.Clear() calls.");
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0,
            $"Coyote found {engine.TestReport.NumOfFoundBugs} arena concurrency bugs. " +
            $"This indicates arena exhaustion or memory corruption under concurrent load.");
    }

    /// <summary>
    /// Tests GetVaultPath for directory creation TOCTOU race.
    /// Kills mutant on line 51 (!Directory.Exists inverted to Directory.Exists).
    ///
    /// Scenario: 5 threads call GetVaultPath when directory doesn't exist.
    /// Expected: Directory is created, all calls succeed, no exceptions.
    /// </summary>
    [Fact]
    public void Coyote_GetVaultPath_Directory_Creation_Race()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(100);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var vaultDir = LuxVault.VaultDirectory;

            // Delete directory to force race condition
            if (Directory.Exists(vaultDir))
            {
                try
                {
                    Directory.Delete(vaultDir, recursive: true);
                }
                catch
                {
                    // Directory in use, skip this test iteration
                    return;
                }
            }

            // 5 threads concurrently calling GetVaultPath
            var tasks = Enumerable.Range(0, 5).Select(i => Task.Run(() =>
            {
                var path = LuxVault.GetVaultPath($"test{i}.vlt");
                path.Should().Contain(vaultDir,
                    $"GetVaultPath should return a path containing vault directory");
            })).ToArray();

            await Task.WhenAll(tasks);

            // Invariant: Directory must exist after concurrent calls
            Directory.Exists(vaultDir).Should().BeTrue(
                "VaultDirectory must be created after concurrent GetVaultPath calls. " +
                "If this fails, the mutant inverted the Directory.Exists check.");
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0,
            $"Coyote found {engine.TestReport.NumOfFoundBugs} directory creation race bugs. " +
            $"This indicates TOCTOU vulnerability or inverted Directory.Exists logic.");
    }

    /// <summary>
    /// Tests concurrent encrypt/decrypt operations for memory safety.
    /// Verifies no memory corruption when multiple threads use LuxVault simultaneously.
    /// Medium priority: Catches memory corruption from removed Array.Clear calls.
    /// </summary>
    [Fact]
    public void Coyote_Concurrent_Encrypt_Decrypt_Memory_Safe()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(100);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            // 20 concurrent operations with different data
            var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            {
                var password = $"password{i % 5}"; // 5 different passwords
                var plaintext = Encoding.UTF8.GetBytes($"TestData{i}");

                var encrypted = LuxVault.Encrypt(plaintext, password);
                encrypted.Should().NotBeNull($"Encryption {i} failed");

                using var decrypted = LuxVault.DecryptToBytes(encrypted, password);
                decrypted.Should().NotBeNull($"Decryption {i} failed");
                decrypted.Span.ToArray().Should().Equal(plaintext,
                    $"Data corruption in operation {i} - wrong plaintext recovered");
            })).ToArray();

            await Task.WhenAll(tasks);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0,
            $"Coyote found {engine.TestReport.NumOfFoundBugs} memory safety bugs under concurrent operations");
    }

    /// <summary>
    /// Tests MixSessionKey for safe concurrent reads.
    /// Verifies that concurrent reads of _sessionKey don't cause corruption.
    /// Medium priority: Catches race conditions in session key mixing.
    /// </summary>
    [Fact]
    public void Coyote_MixSessionKey_Concurrent_Reads_Safe()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(100);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            // Initialize session key once
            byte[] sessionKey = new byte[32];
            RandomNumberGenerator.Fill(sessionKey);
            LuxVault.InitializeSessionKey(sessionKey);

            // 10 concurrent encrypt operations (all use MixSessionKey internally)
            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                var plaintext = Encoding.UTF8.GetBytes($"Data{i}");
                var encrypted = LuxVault.Encrypt(plaintext, "password");

                using var decrypted = LuxVault.DecryptToBytes(encrypted, "password");
                decrypted.Should().NotBeNull();
                decrypted.Span.ToArray().Should().Equal(plaintext);
            })).ToArray();

            await Task.WhenAll(tasks);
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0,
            $"Coyote found {engine.TestReport.NumOfFoundBugs} race conditions in session key mixing");
    }
}

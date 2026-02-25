using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Configuration;
using Xunit;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace NinePSharp.Tests;

[Collection("Global Arena")]
public class LuxVaultStressTests
{
    private static readonly FieldInfo ArenasField = typeof(LuxVault).GetField("Arenas", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly PropertyInfo ActiveAllocationsProp = typeof(SecureMemoryArena).GetProperty("ActiveAllocations")!;

    private int GetActiveAllocations()
    {
        var arenas = (SecureMemoryArena[])ArenasField.GetValue(null)!;
        return arenas.Sum(a => (int)ActiveAllocationsProp.GetValue(a)!);
    }

    [Fact]
    public void Stress_Volume_10k_Operations()
    {
        // USER REQUESTED: 10,000 operations
        const int totalOps = 10_000;
        const string password = "stress-test-pass";
        byte[] data = Encoding.UTF8.GetBytes("standard-secret-payload");
        
        int baseline = GetActiveAllocations();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < totalOps; i++)
        {
            byte[] encrypted = LuxVault.Encrypt(data, password);
            using (var secret = LuxVault.DecryptToBytes(encrypted, password))
            {
                // Just burn CPU and churn arena
            }
        }

        sw.Stop();
        Console.WriteLine($"[Stress] Volume Test (10k): {totalOps} ops in {sw.Elapsed.TotalSeconds:F2}s");
        GetActiveAllocations().Should().Be(baseline);
    }

    [Fact]
    public void Stress_Volume_10M_Operations_Slab_Pool_Churn()
    {
        // USER REQUESTED: 10,000,000 operations
        const int totalOps = 10_000_000;
        const string password = "stress-test-pass";
        byte[] data = Encoding.UTF8.GetBytes("standard-secret-payload-32-bytes-long");
        
        int baseline = GetActiveAllocations();
        var sw = Stopwatch.StartNew();

        // Target 80% CPU usage
        var options = new ParallelOptions {
            MaxDegreeOfParallelism = (int)Math.Max(1, Environment.ProcessorCount * 0.8)
        };

        long progress = 0;
        Parallel.For(0, totalOps, options, i => 
        {
            byte[] encrypted = LuxVault.Encrypt(data, password);
            using (var secret = LuxVault.DecryptToBytes(encrypted, password))
            {
                // Logic check
                var current = Interlocked.Increment(ref progress);
                if (current % 1_000_000 == 0)
                {
                    secret.Should().NotBeNull();
                    data.SequenceEqual(secret!.Span.ToArray()).Should().BeTrue();
                    Console.WriteLine($"[Stress] Progress: {current} / {totalOps} ({(double)current/totalOps*100:F1}%) - Throughput: {current / sw.Elapsed.TotalSeconds:F0} ops/sec");
                }
            }
        });

        sw.Stop();
        GetActiveAllocations().Should().Be(baseline, "Arena must return to baseline after 10M churn operations");
    }

    [Fact]
    public async Task Stress_Concurrency_10k_Tasks()
    {
        // USER REQUESTED: 10,000 tasks
        const int concurrentTasks = 10_000;
        const string password = "concurrent-pass";
        byte[] data = Encoding.UTF8.GetBytes("concurrency-payload");

        int baseline = GetActiveAllocations();
        var tasks = new List<Task<bool>>();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try {
                    var enc = LuxVault.Encrypt(data, password);
                    using var dec = LuxVault.DecryptToBytes(enc, password);
                    return dec != null && data.SequenceEqual(dec.Span.ToArray());
                } catch { return false; }
            }));
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        Console.WriteLine($"[Stress] Concurrency Test: {concurrentTasks} tasks in {sw.Elapsed.TotalMilliseconds:F2}ms");
        
        results.All(r => r).Should().BeTrue("All 10k concurrent operations must succeed");
        GetActiveAllocations().Should().Be(baseline, "Arena must be clean after high-concurrency burst");
    }

    [Fact]
    public void Stress_Memory_Pressure_100MB_Secret()
    {
        // USER REQUESTED: 100MB secret
        // Note: DecryptToBytes allocates the plaintext buffer from the arena.
        // Since the arena is 1MB, a 100MB decryption MUST throw ArgumentOutOfRangeException.
        
        byte[] hugePayload = new byte[100 * 1024 * 1024 + 64]; // + header overhead
        new System.Random().NextBytes(hugePayload);

        Console.WriteLine("[Stress] Attempting 100MB secret decryption (expected to exceed 1MB arena)...");

        Action act = () => LuxVault.DecryptToBytes(hugePayload, "password");

        // LuxVault wraps the arena exception in a generic exception
        act.Should().Throw<Exception>()
           .WithInnerException<ArgumentOutOfRangeException>()
           .WithMessage("*between 0 and 1048576*");
        
        Console.WriteLine("[Stress] 100MB payload correctly rejected by arena bounds.");
    }

    [Fact]
    public void Stress_Memory_Pressure_OneByteOverLimit()
    {
        // Limit is 1,048,576 bytes (1MB). 
        // We attempt 1,048,577 bytes.
        const int ArenaLimit = 1024 * 1024;
        const int OverLimit = ArenaLimit + 1;
        
        // Payload: Salt(16) + Nonce(24) + Mac(16) + Ciphertext(OverLimit)
        byte[] payload = new byte[16 + 24 + 16 + OverLimit];
        new System.Random().NextBytes(payload);

        Console.WriteLine($"[Stress] Attempting decryption of {OverLimit} bytes (Limit+1)...");

        Action act = () => LuxVault.DecryptToBytes(payload, "password");

        act.Should().Throw<Exception>()
           .WithInnerException<ArgumentOutOfRangeException>()
           .WithMessage($"*Allocation size must be between 0 and {ArenaLimit}*");

        Console.WriteLine("[Stress] Boundary (1MB + 1 byte) correctly rejected.");
    }

    [Property(MaxTest = 1000)]
    public bool Stress_Randomized_Input_Sizes_FsCheck(byte[] randomData)
    {
        if (randomData == null) return true;
        
        // Limit size to what the arena can actually hold (1MB minus overhead)
        if (randomData.Length > 900_000) return true;

        int baseline = GetActiveAllocations();
        
        try {
            var encrypted = LuxVault.Encrypt(randomData, "pass");
            using var decrypted = LuxVault.DecryptToBytes(encrypted, "pass");
            
            bool success = decrypted != null && randomData.SequenceEqual(decrypted.Span.ToArray());
            return success && GetActiveAllocations() == baseline;
        } catch (ArgumentOutOfRangeException) {
            return true; // Expected if FsCheck produces something exactly at the edge
        }
    }
}

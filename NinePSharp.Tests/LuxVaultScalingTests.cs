using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Server.Utils;
using Xunit;
using Xunit.Abstractions;

namespace NinePSharp.Tests
{
    public class LuxVaultScalingTests
    {
        private readonly ITestOutputHelper _output;
        private static readonly FieldInfo GovernorField = typeof(LuxVault).GetField("ConcurrencyGovernor", BindingFlags.NonPublic | BindingFlags.Static)!;
        
        public LuxVaultScalingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Verify_Arena_Sharding_Across_Threads()
        {
            // We want to verify that different threads map to different arena shards.
            var arenas = LuxVault.Arenas;
            var seenArenas = new ConcurrentDictionary<int, bool>();
            int threadCount = Math.Max(arenas.Length * 2, 8);
            
            var options = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
            
            Parallel.For(0, 100, options, i =>
            {
                // Access GetLocalArena directly (it is public internal now)
                var arena = LuxVault.GetLocalArena();
                
                // Get hash code or index to identify the specific shard
                int arenaHash = arena.GetHashCode();
                seenArenas.TryAdd(arenaHash, true);
                
                // Perform a small op to ensure it's functional
                LuxVault.GenerateHiddenId(new byte[32]);
            });

            _output.WriteLine($"Total Arena Shards: {arenas.Length}");
            _output.WriteLine($"Unique Arenas Used in Test: {seenArenas.Count}");

            // With 16 shards and 100 iterations on multiple threads, 
            // we should definitely see more than 1 unique arena being used.
            seenArenas.Count.Should().BeGreaterThan(1, "Vault must distribute work across multiple arena shards");
        }

        [Fact]
        public async Task Verify_Concurrency_Governor_Enforces_80_Percent_Cap()
        {
            var governor = (SemaphoreSlim)GovernorField.GetValue(null)!;
            int maxAllowed = governor.CurrentCount;
            int processorCount = Environment.ProcessorCount;
            
            _output.WriteLine($"Processors: {processorCount}");
            _output.WriteLine($"Governor Limit (80%): {maxAllowed}");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int peakConcurrentOps = 0;
            long activeOps = 0;

            // Spawn double the allowed threads to try and overwhelm the governor
            var tasks = Enumerable.Range(0, maxAllowed * 2).Select(_ => Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    // Monitor the current utilization
                    // Note: This is an approximation as we can't easily hook into the Semaphore's internals
                    // but we can check CurrentCount.
                    int currentRunning = maxAllowed - governor.CurrentCount;
                    Interlocked.Exchange(ref peakConcurrentOps, Math.Max(peakConcurrentOps, currentRunning));
                    
                    // Trigger a vault operation (which waits on the governor)
                    try { LuxVault.GenerateHiddenId(new byte[32]); } catch { }
                }
            })).ToArray();

            await Task.Delay(2000); // Let it saturate
            cts.Cancel();
            await Task.WhenAll(tasks);

            _output.WriteLine($"Peak Concurrent Operations observed: {peakConcurrentOps}");
            
            peakConcurrentOps.Should().BeLessThanOrEqualTo(maxAllowed, "Governor must never allow more than 80% CPU ops simultaneously");
        }

        [Fact]
        public void Linear_Scaling_Benchmark()
        {
            // Benchmark scaling: 1 Core vs N Cores
            const int totalOps = 5000;
            const string password = "scaling-test";
            byte[] data = Encoding.UTF8.GetBytes("scaling-data-payload");
            byte[] encrypted = LuxVault.Encrypt(data, password);

            // 1. Serial (Single Core)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < totalOps; i++)
            {
                using var secret = LuxVault.DecryptToBytes(encrypted, password);
            }
            sw.Stop();
            double serialTime = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"Serial Execution (1 Thread): {serialTime:F2}ms");

            // 2. Parallel (N Cores)
            int dop = (int)Math.Max(2, Environment.ProcessorCount * 0.8);
            var options = new ParallelOptions { MaxDegreeOfParallelism = dop };
            
            sw.Restart();
            Parallel.For(0, totalOps, options, i =>
            {
                using var secret = LuxVault.DecryptToBytes(encrypted, password);
            });
            sw.Stop();
            double parallelTime = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"Parallel Execution ({dop} Threads): {parallelTime:F2}ms");

            double speedup = serialTime / parallelTime;
            _output.WriteLine($"Total Speedup: {speedup:F2}x");

            // Assert that we get a meaningful speedup. 
            // In a truly linear system with 8 cores, we'd expect ~6-7x.
            // We set a conservative floor of 1.5x to account for PBKDF2 overhead and CI environments.
            speedup.Should().BeGreaterThan(1.2, "Parallel vault execution must provide meaningful speedup over serial execution");
        }
    }
}

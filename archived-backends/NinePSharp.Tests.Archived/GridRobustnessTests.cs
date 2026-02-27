using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Backends.Pipes;
using NinePSharp.Backends.PowerShell;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using Xunit.Abstractions;

namespace NinePSharp.Tests
{
    public class GridRobustnessTests
    {
        private readonly ITestOutputHelper _output;

        public GridRobustnessTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region LuxVault Security Fuzzing

        [Property(MaxTest = 100, DisplayName = "LuxVault: Decrypt should always return null for corrupted ciphertexts")]
        public bool LuxVault_MAC_Corruption_Fuzz_Property(byte[] plaintext, string password)
        {
            if (plaintext == null || password == null) return true;

            // 1. Encrypt validly
            byte[] ciphertext = LuxVault.Encrypt(plaintext, password);
            if (ciphertext.Length < 32) return true; // Skip too small

            // 2. Corrupt exactly one random byte in the MAC or Ciphertext region
            // (Salt is first 16 bytes, Nonce is next 24, MAC is next 16)
            var rng = new Random();
            int indexToCorrupt = rng.Next(16, ciphertext.Length);
            ciphertext[indexToCorrupt] ^= 0xFF;

            // 3. Decrypt - Must return null, NEVER throw or return partial data
            try
            {
                using var decrypted = LuxVault.DecryptToBytes(ciphertext, password);
                return decrypted == null;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"LuxVault threw during fuzzing: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IPC Engine (Pipes & Queues) Robustness

        [Property(DisplayName = "ObjectQueue: Should maintain atomicity for fuzzed payloads")]
        public bool ObjectQueue_Atomicity_Fuzz_Property(byte[][] messages)
        {
            if (messages == null || messages.Length == 0) return true;

            var queue = new ObjectQueueNode("fuzz_queue");
            
            // 1. Write all messages
            foreach (var m in messages)
            {
                if (m == null) continue;
                queue.WriteAsync(m).Wait();
            }

            // 2. Read back and verify exact matches
            foreach (var original in messages)
            {
                if (original == null) continue;
                var readTask = queue.ReadAsync(1024 * 1024);
                readTask.Wait();
                var recovered = readTask.Result.ToArray();
                
                if (!original.SequenceEqual(recovered)) return false;
            }

            return true;
        }

        [Fact]
        public async Task DataPipe_Concurrent_Fragmentation_Stress()
        {
            var pipe = new DataPipeNode("stress_pipe");
            const int totalBytes = 1024 * 1024; // 1MB
            byte[] sourceData = new byte[totalBytes];
            RandomNumberGenerator.Fill(sourceData);

            // 1. Writer: Pushes data in random chunk sizes
            var writeTask = Task.Run(async () =>
            {
                int sent = 0;
                var rng = new Random();
                while (sent < totalBytes)
                {
                    int chunk = rng.Next(1, 8192);
                    int remaining = totalBytes - sent;
                    int actual = Math.Min(chunk, remaining);
                    await pipe.WriteAsync(sourceData.AsMemory(sent, actual));
                    sent += actual;
                }
            });

            // 2. Reader: Pulls data in random chunk sizes
            byte[] recoveredData = new byte[totalBytes];
            var readTask = Task.Run(async () =>
            {
                int received = 0;
                var rng = new Random();
                while (received < totalBytes)
                {
                    uint request = (uint)rng.Next(1, 4096);
                    var chunk = await pipe.ReadAsync(request);
                    chunk.ToArray().CopyTo(recoveredData, received);
                    received += chunk.Length;
                }
            });

            await Task.WhenAll(writeTask, readTask);
            recoveredData.Should().Equal(sourceData, "Data Pipe must reassemble fragmented stream perfectly");
        }

        #endregion

        #region PowerShell Isolation & Failure Paths

        [Fact]
        public async Task PowerShell_Process_Crash_Returns_Failure_Status()
        {
            var job = new PowerShellJob("crash-job");
            // Command that exits with non-zero
            job.Script = "[System.Environment]::Exit(1)";
            
            await job.RunAsync();
            
            job.Status.Should().Be("Failed");
            job.Errors.ToString().Should().Contain("exited with code: 1");
        }

        [Fact]
        public async Task PowerShell_Syntax_Error_Captured_In_Errors_File()
        {
            var job = new PowerShellJob("syntax-job");
            job.Script = "This Is Not Valid PowerShell !!! @@@";
            
            await job.RunAsync();
            
            job.Status.Should().Be("Failed");
            job.Errors.ToString().Should().NotBeEmpty();
        }

        #endregion

        #region SecureMemoryArena Boundary Testing

        [Fact]
        public async Task Arena_Exhaustion_Does_Not_Crash_Server()
        {
            // We use the sharded Arenas pool
            var arena = LuxVault.GetLocalArena();
            var allocations = new List<Memory<byte>>();

            try
            {
                // 1. Attempt to exhaust the 1MB arena shard
                // (Allocating 128 chunks of 10KB = 1.28MB)
                for (int i = 0; i < 150; i++)
                {
                    try { allocations.Add(arena.Allocate(10240).ToArray()); }
                    catch (Exception ex) when (ex is OutOfMemoryException || ex is InvalidOperationException)
                    {
                        // Expected failure when shard is full.
                    }
                }

                // 2. Verify we can still perform small allocations in OTHER shards
                // By switching threads, we hit a different shard.
                bool otherShardWorks = false;
                await Task.Run(() =>
                {
                    var otherArena = LuxVault.GetLocalArena();
                    var span = otherArena.Allocate(100);
                    otherShardWorks = span.Length == 100;
                    otherArena.Free(span);
                });

                otherShardWorks.Should().BeTrue("Exhausting one arena shard should not block other threads");
            }
            finally
            {
                // Cleanup
                foreach (var a in allocations) arena.Free(a.Span);
            }
        }

        #endregion
    }
}

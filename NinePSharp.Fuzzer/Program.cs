using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SharpFuzz;
using NinePSharp.Server.Backends;
using NinePSharp.Backends.Pipes;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using Moq;
using Moq.Protected;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using static NinePSharp.Fuzzer.SecureMemoryArenaUnsafeAccessors;

namespace NinePSharp.Fuzzer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "pipes")
            {
                FuzzPipes();
            }
            else if (args.Length > 0 && (args[0] == "blockchain" || args[0] == "chain"))
            {
                FuzzBlockchain();
            }
            else if (args.Length > 0 && args[0] == "mock")
            {
                FuzzMockFileSystem();
            }
            else if (args.Length > 0 && args[0] == "secret")
            {
                FuzzSecretFileSystem();
            }
            else if (args.Length > 0 && args[0] == "securebuffer")
            {
                FuzzSecureBuffer();
            }
            else if (args.Length > 0 && args[0] == "securebuffer-race")
            {
                FuzzSecureBufferRaceConditions();
            }
            else
            {
                FuzzParser();
            }
        }

        private static void FuzzParser()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        NinePSharp.Parser.NinePParser.parse(NinePDialect.NineP2000U, data.AsMemory());
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzBlockchain()
        {
            var vault = new LuxVaultService();

            INinePFileSystem NewBitcoin() => new BitcoinFileSystem(new BitcoinBackendConfig { Network = "Main" }, null, vault);
            INinePFileSystem NewEthereum() => new EthereumFileSystem(new EthereumBackendConfig { RpcUrl = "http://localhost" }, null!, vault);
            INinePFileSystem NewSolana() => new SolanaFileSystem(new SolanaBackendConfig(), null, vault);
            INinePFileSystem NewStellar() => new StellarFileSystem(new StellarBackendConfig(), null, vault);
            INinePFileSystem NewCardano() => new CardanoFileSystem(new CardanoBackendConfig(), vault);

            var factories = new Func<INinePFileSystem>[]
            {
                NewBitcoin,
                NewEthereum,
                NewSolana,
                NewStellar,
                NewCardano
            };

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    var text = System.Text.Encoding.UTF8.GetString(data);

                    var fs = factories[(data.Length == 0 ? 0 : data[0]) % factories.Length]();
                    var pathParts = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);

                    fs.WalkAsync(new Twalk(1, 0, 1, pathParts)).Wait();
                    fs.OpenAsync(new Topen(1, 1, 0)).Wait();
                    fs.WriteAsync(new Twrite(1, 1, 0, data)).Wait();
                    fs.ReadAsync(new Tread(1, 1, 0, 8192)).Wait();
                    fs.StatAsync(new Tstat(1, 1)).Wait();

                    var clone = fs.Clone();
                    clone.StatAsync(new Tstat(1, 1)).Wait();
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }

        private static void FuzzMockFileSystem()
        {
            var vault = new LuxVaultService();

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0) return;

                        var fs = new MockFileSystem(vault);
                        var text = System.Text.Encoding.UTF8.GetString(data);

                        // Extract filename from fuzzed data
                        var fileName = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries)
                                           .FirstOrDefault() ?? "fuzzfile.txt";

                        // Fuzz StatfsAsync
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 1, 1)).Wait();

                        // Fuzz file creation
                        fs.LcreateAsync(new Tlcreate(100, 1, 1, fileName, 0, 0644, 0)).Wait();

                        // Fuzz directory creation
                        if (data.Length > 1 && data[1] % 2 == 0)
                        {
                            fs.MkdirAsync(new Tmkdir(100, 2, 1, fileName + "_dir", 0755, 0)).Wait();
                        }

                        // Fuzz walk
                        fs.WalkAsync(new Twalk(1, 1, 2, new[] { fileName })).Wait();

                        // Fuzz write/read
                        fs.WriteAsync(new Twrite(1, 2, 0, data)).Wait();
                        fs.ReadAsync(new Tread(1, 2, 0, (uint)Math.Min(data.Length, 8192))).Wait();

                        // Fuzz rename
                        if (data.Length > 2)
                        {
                            fs.RenameAsync(new Trename(100, 3, 2, 1, fileName + "_renamed")).Wait();
                        }

                        // Fuzz renameat
                        if (data.Length > 3)
                        {
                            fs.RenameatAsync(new Trenameat(100, 4, 1, fileName, 1, fileName + "_new")).Wait();
                        }

                        // Fuzz readdir
                        fs.ReaddirAsync(new Treaddir(100, 5, 1, 0, 8192)).Wait();

                        // Fuzz clone
                        var clone = fs.Clone();
                        ((MockFileSystem)clone).StatfsAsync(new Tstatfs(100, 1, 1)).Wait();
                    }
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }

        private static void FuzzSecretFileSystem()
        {
            var vault = new LuxVaultService();
            var config = new SecretBackendConfig();

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0) return;

                        var fs = new SecretFileSystem(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, config, vault);
                        var text = System.Text.Encoding.UTF8.GetString(data);

                        // Fuzz StatfsAsync at root
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 1, 1)).Wait();

                        // Fuzz ReaddirAsync at root
                        fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192)).Wait();

                        // Fuzz walk to vault
                        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();

                        // Fuzz StatfsAsync in vault
                        fs.StatfsAsync(new Tstatfs((ushort)(data[0] % 256), 2, 2)).Wait();

                        // Fuzz ReaddirAsync in vault
                        fs.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192)).Wait();

                        // Fuzz walk to provision
                        fs.WalkAsync(new Twalk(2, 2, 3, new[] { "..", "provision" })).Wait();

                        // Fuzz write to provision (secret provisioning)
                        fs.WriteAsync(new Twrite(1, 3, 0, data)).Wait();

                        // Fuzz walk to unlock
                        fs.WalkAsync(new Twalk(3, 3, 4, new[] { "..", "unlock" })).Wait();

                        // Fuzz write to unlock
                        fs.WriteAsync(new Twrite(1, 4, 0, data)).Wait();

                        // Fuzz StatAsync
                        fs.StatAsync(new Tstat(1, 1)).Wait();

                        // Fuzz clone
                        var clone = fs.Clone();
                        ((SecretFileSystem)clone).StatfsAsync(new Tstatfs(100, 1, 1)).Wait();

                        // Fuzz readdir with different offsets
                        if (data.Length > 1)
                        {
                            var offset = (ulong)(data[1] % 100);
                            fs.ReaddirAsync(new Treaddir(100, 1, 1, offset, 8192)).Wait();
                        }
                    }
                }
                catch (Exception)
                {
                    // Fuzzing expects protocol/parser exceptions; crash-only signal is handled by SharpFuzz.
                }
            });
        }

        private static void FuzzSecureBuffer()
        {
            Console.WriteLine("=== SecureBuffer Vulnerability Fuzzer ===");
            Console.WriteLine("Testing double-free and slab corruption vulnerabilities...");

            var random = new Random();
            var aliasDetected = 0;
            var slabCorruptionDetected = 0;
            var iterations = 0;

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length < 4) return;

                        using var arena = new SecureMemoryArena(1024 * 1024);
                        iterations++;

                        // Extract fuzzing parameters from input
                        int allocSize = Math.Max(16, Math.Min(4096, data[0] * 16));
                        bool doubleFree = (data[1] % 2) == 0;
                        bool sliceFree = (data[2] % 2) == 0;
                        int sliceSize = Math.Max(1, Math.Min(allocSize, data[3] * 8));

                        // Test 1: Double-free vulnerability
                        if (doubleFree)
                        {
                            var buf1 = new SecureBuffer(allocSize, arena);
                            IntPtr originalPtr = GetPointer(buf1.Span);

                            // Dispose twice
                            DisposeBuffer(buf1);
                            buf1.Dispose();

                            // Allocate twice - should get different pointers
                            var buf2 = new SecureBuffer(allocSize, arena);
                            var buf3 = new SecureBuffer(allocSize, arena);

                            IntPtr ptr2 = GetPointer(buf2.Span);
                            IntPtr ptr3 = GetPointer(buf3.Span);

                            if (ptr2 == ptr3)
                            {
                                Interlocked.Increment(ref aliasDetected);
                                Console.WriteLine($"[VULN] Memory aliasing detected! Size={allocSize}, Aliases={aliasDetected}");
                            }

                            buf2.Dispose();
                            buf3.Dispose();
                        }

                        // Test 2: Sliced free vulnerability
                        if (sliceFree && sliceSize < allocSize)
                        {
                            var buf = new SecureBuffer(allocSize, arena);
                            IntPtr originalPtr = GetPointer(buf.Span);

                            // Slice and free
                            var slice = buf.Span.Slice(0, sliceSize);
                            FreeSlice(arena, slice);

                            // Allocate with slice size
                            var bufSmall = new SecureBuffer(sliceSize, arena);
                            IntPtr ptrSmall = GetPointer(bufSmall.Span);

                            // Check if we got a mismatched pointer
                            if (originalPtr == ptrSmall)
                            {
                                Interlocked.Increment(ref slabCorruptionDetected);
                                Console.WriteLine($"[VULN] Slab corruption! AllocSize={allocSize}, SliceSize={sliceSize}, Corruptions={slabCorruptionDetected}");
                            }

                            bufSmall.Dispose();
                        }

                        // Test 3: Combined stress
                        for (int i = 0; i < Math.Min(10, data.Length); i++)
                        {
                            int size = Math.Max(16, data[i] % 256);
                            var buf = new SecureBuffer(size, arena);

                            // Random vulnerability pattern
                            switch (data[i] % 3)
                            {
                                case 0: // Double-free
                                    DisposeBuffer(buf);
                                    buf.Dispose();
                                    break;
                                case 1: // Sliced free
                                    if (size > 16)
                                    {
                                        FreeSlice(arena, buf.Span.Slice(0, size / 2));
                                    }
                                    break;
                                case 2: // Normal
                                    buf.Dispose();
                                    break;
                            }
                        }

                        if (iterations % 1000 == 0)
                        {
                            Console.WriteLine($"Iterations: {iterations}, Aliases: {aliasDetected}, Corruptions: {slabCorruptionDetected}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRASH] {ex.GetType().Name}: {ex.Message}");
                    throw; // Let SharpFuzz detect crashes
                }
            });
        }

        private static void FuzzSecureBufferRaceConditions()
        {
            Console.WriteLine("=== SecureBuffer Concurrent Race Condition Fuzzer ===");
            Console.WriteLine("Testing for race conditions under parallel load...");

            var aliasDetected = 0;
            var totalTests = 0;

            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length < 8) return;

                        using var arena = new SecureMemoryArena(4 * 1024 * 1024); // 4MB
                        Interlocked.Increment(ref totalTests);

                        // Extract concurrency parameters
                        int threadCount = Math.Max(2, Math.Min(16, (int)data[0] % 16));
                        int opsPerThread = Math.Max(10, Math.Min(100, (int)data[1]));

                        var tasks = new Task[threadCount];
                        var detectedAliases = new System.Collections.Concurrent.ConcurrentBag<(IntPtr, IntPtr)>();

                        for (int t = 0; t < threadCount; t++)
                        {
                            int threadId = t;
                            tasks[t] = Task.Run(() =>
                            {
                                var random = new Random(threadId + data[2]);

                                for (int i = 0; i < opsPerThread; i++)
                                {
                                    int idx = (threadId * opsPerThread + i) % data.Length;
                                    int size = Math.Max(32, data[idx] % 256);

                                    long handle;
                                    Span<byte> span = arena.Allocate(size, out handle);

                                    // Write unique pattern
                                    for (int j = 0; j < Math.Min(size, 8); j++)
                                    {
                                        span[j] = (byte)(threadId + i + j);
                                    }

                                    // Random vulnerability injection
                                    switch (data[idx] % 4)
                                    {
                                        case 0: // Double-dispose race
                                            // Simulate race by calling Free on handle twice from separate tasks
                                            Task.Run(() => arena.Free(handle));
                                            Task.Run(() => arena.Free(handle));
                                            Thread.Sleep(1);
                                            break;

                                        case 1: // Sliced free race
                                            if (size > 16)
                                            {
                                                // arena.Free(Span) is the legacy vulnerable overload
                                                var sliced = span.Slice(0, size / 2);
                                                FreeSlice(arena, sliced);
                                                FreeSlice(arena, span);
                                            }
                                            Thread.Sleep(1);
                                            break;

                                        case 2: // Normal dispose
                                            arena.Free(handle);
                                            break;

                                        case 3: // Use-after-free
                                            arena.Free(handle);
                                            Thread.Sleep(1);
                                            // Try to access after dispose (undefined behavior/potential crash if not RAM-locked)
                                            try { var _ = span[0]; } catch { }
                                            break;
                                    }

                                    // Allocate pair and check for aliasing
                                    if (i % 10 == 0)
                                    {
                                        long hA, hB;
                                        Span<byte> spanA = arena.Allocate(size, out hA);
                                        Span<byte> spanB = arena.Allocate(size, out hB);

                                        IntPtr ptrA = GetPointer(spanA);
                                        IntPtr ptrB = GetPointer(spanB);

                                        if (ptrA == ptrB)
                                        {
                                            detectedAliases.Add((ptrA, ptrB));
                                            Interlocked.Increment(ref aliasDetected);
                                        }

                                        arena.Free(hA);
                                        arena.Free(hB);
                                    }
                                }
                            });
                        }

                        Task.WaitAll(tasks);

                        if (!detectedAliases.IsEmpty)
                        {
                            Console.WriteLine($"[VULN] Race condition exposed {detectedAliases.Count} memory aliases! (Total: {aliasDetected})");
                        }

                        if (totalTests % 100 == 0)
                        {
                            Console.WriteLine($"Race tests: {totalTests}, Total aliases: {aliasDetected}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRASH] {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            });
        }

        private static unsafe IntPtr GetPointer(Span<byte> span)
        {
            if (span.IsEmpty) return IntPtr.Zero;
            fixed (byte* p = span)
            {
                return (IntPtr)p;
            }
        }

        private static void DisposeBuffer(SecureBuffer buffer)
        {
            // Helper to dispose a buffer (simulates passing by value)
            buffer.Dispose();
        }

        private static Tcreate BuildCreateMessage(ushort tag, uint fid, string name, uint perm = 0755, byte mode = NinePConstants.ORDWR)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameBytes.Length + 4 + 1);
            var buffer = new byte[size];

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), size);
            buffer[4] = (byte)MessageTypes.Tcreate;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(5, 2), tag);

            int offset = NinePConstants.HeaderSize;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), fid);
            offset += 4;

            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), (ushort)nameBytes.Length);
            offset += 2;
            nameBytes.CopyTo(buffer.AsSpan(offset));
            offset += nameBytes.Length;

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), perm);
            offset += 4;
            buffer[offset] = mode;

            return new Tcreate(buffer);
        }

        private static void FuzzPipes()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                var fs = new PipeFileSystem();
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length < 5) return;

                        // Use first byte to decide action
                        int action = data[0] % 3;
                        string qName = "fuzz_target";

                        // Setup: Walk to queues and create a queue
                        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "queues" })).Wait();
                        fs.CreateAsync(BuildCreateMessage(1, 2, qName)).Wait();
                        fs.WalkAsync(new Twalk(1, 2, 3, new[] { qName })).Wait();

                        switch (action)
                        {
                            case 0: // Heavy Writes
                                fs.WriteAsync(new Twrite(1, 3, 0, data)).Wait();
                                break;
                            case 1: // Reads
                                fs.ReadAsync(new Tread(1, 3, 0, (uint)data.Length)).Wait();
                                break;
                            case 2: // Concurrent-ish sequence
                                var t1 = fs.WriteAsync(new Twrite(1, 3, 0, data.Take(data.Length/2).ToArray()));
                                var t2 = fs.ReadAsync(new Tread(1, 3, 0, 1024));
                                Task.WaitAll(t1, t2);
                                break;
                        }
                    }
                }
                catch (Exception) { }
            });
        }
    }
}

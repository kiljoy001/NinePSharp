using NinePSharp.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class ArenaPropertyTests
{
    [Property(MaxTest = 100)]
    public bool Arena_Allocation_Consistency_Property(int[] sizes)
    {
        if (sizes == null || sizes.Length == 0) return true;
        
        // Filter sizes to be reasonable (1 to 1024 bytes) and limit total count
        var validSizes = sizes.Select(s => Math.Abs(s) % 1024 + 1).Take(100).ToArray();
        
        using var arena = new SecureMemoryArena(1024 * 64); // 64KB test arena
        
        foreach (var size in validSizes)
        {
            var span = arena.Allocate(size, out long handle);
            if (span.Length != size) return false;
            
            // Write some data to ensure it's writable
            span.Fill(0xAA);
            arena.Free(handle);
        }
        
        return true;
    }

    [Fact]
    public void Arena_Concurrent_Access_Stability()
    {
        // Property testing concurrency is tricky, so we use a high-load Fact
        using var arena = new SecureMemoryArena(1024 * 1024); // 1MB
        int taskCount = 20;
        int iterationsPerTask = 500;

        Parallel.For(0, taskCount, _ =>
        {
            var rand = new Random();
            for (int i = 0; i < iterationsPerTask; i++)
            {
                int size = rand.Next(1, 512);
                var span = arena.Allocate(size, out long handle);
                
                // Assertions inside parallel tasks can be noisy, but let's check basic validity
                if (span.Length != size) throw new Exception("Invalid span length");
                
                span.Fill((byte)(i % 255));
                arena.Free(handle);
            }
        });
    }

    [Property]
    public bool Arena_Pool_Reuse_Property(int size)
    {
        size = Math.Abs(size) % 1024 + 1;
        using var arena = new SecureMemoryArena(1024 * 10);
        
        // Allocate and free 10 times with the same size
        // This should exercise the ConcurrentStack pool
        for (int i = 0; i < 10; i++)
        {
            var span = arena.Allocate(size, out long handle);
            arena.Free(handle);
        }
        
        return true;
    }
}

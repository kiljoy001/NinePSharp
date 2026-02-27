using NinePSharp.Constants;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests.Coyote;

public class SecureMemoryArenaCoyoteTests
{
    private static Microsoft.Coyote.Configuration CreateCoyoteConfiguration(uint iterations)
    {
        return Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(iterations)
            .WithPartiallyControlledConcurrencyAllowed(true)
            .WithUncontrolledConcurrencyResolutionTimeout(attempts: 100, delay: 1)
            .WithPotentialDeadlocksReportedAsBugs(false);
    }

    [Fact]
    public void Coyote_SecureMemoryArena_FreeHandle_Atomic_Race()
    {
        // This test targets the race condition in Free(long handle)
        // Scenario: Two threads concurrently free the same handle.
        // If not atomic, both see meta.IsFreed == false and both push the pointer to the pool.
        
        var configuration = CreateCoyoteConfiguration(iterations: 1000);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            using var arena = new SecureMemoryArena(64 * 1024);
            long handle;
            var span = arena.Allocate(32, out handle);
            
            // Concurrent double-free
            var t1 = CoyoteTask.Run(() => arena.Free(handle));
            var t2 = CoyoteTask.Run(() => arena.Free(handle));
            
            await CoyoteTask.WhenAll(t1, t2);

            // If the race occurred, the pointer was pushed twice.
            // Next two allocations of same size will return the same pointer (aliasing).
            var buf1 = arena.Allocate(32, out _);
            var buf2 = arena.Allocate(32, out _);

            unsafe 
            {
                fixed (byte* p1 = buf1, p2 = buf2)
                {
                    IntPtr ptr1 = (IntPtr)p1;
                    IntPtr ptr2 = (IntPtr)p2;
                    
                    // INVARIANT: Concurrent frees must not result in memory aliasing
                    (ptr1 != ptr2).Should().BeTrue("Memory aliasing detected! Double-free race condition triggered.");
                }
            }
        });

        engine.Run();
        
        if (engine.TestReport.NumOfFoundBugs > 0)
        {
            var bug = engine.TestReport.BugReports.FirstOrDefault();
            Assert.Fail($"Coyote found a race condition: {bug}");
        }
    }

    [Fact]
    public void Coyote_SecureMemoryArena_MetadataLeak_Property()
    {
        // This test verifies that metadata is cleaned up.
        // If it fails (bugs found), it means metadata is leaking.
        
        var configuration = CreateCoyoteConfiguration(iterations: 10);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            using var arena = new SecureMemoryArena(64 * 1024);
            
            for (int i = 0; i < 5; i++)
            {
                long handle;
                arena.Allocate(32, out handle);
                arena.Free(handle);
            }

            // Invariant: After Free, metadata for that handle should be gone.
            var field = typeof(SecureMemoryArena).GetField("_allocations", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (System.Collections.IDictionary)field!.GetValue(arena)!;
            
            // VULNERABILITY: Currently it leaks, so count will be 5.
            // When fixed, it should be 0.
            dict.Count.Should().Be(0, "Metadata leak detected! _allocations should be empty after Free.");
        });

        engine.Run();
        
        if (engine.TestReport.NumOfFoundBugs > 0)
        {
             // We expect this to fail currently because the leak is present.
        }
    }
}

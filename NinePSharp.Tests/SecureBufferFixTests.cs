using NinePSharp.Constants;
using System;
using System.Linq;
using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class SecureBufferFixTests : IDisposable
{
    private readonly SecureMemoryArena _arena;

    public SecureBufferFixTests()
    {
        _arena = new SecureMemoryArena(1024 * 1024);
    }

    public void Dispose()
    {
        _arena?.Dispose();
    }

    [Fact]
    public void DoubleFree_WithHandles_DoesNotCorrupt()
    {
        var buf1 = new SecureBuffer(64, _arena);
        IntPtr ptr1 = GetPointer(buf1.Span);

        // Dispose twice
        buf1.Dispose();
        buf1.Dispose(); // Should be no-op

        // Allocate again - should get different or same pointer safely
        var buf2 = new SecureBuffer(64, _arena);
        var buf3 = new SecureBuffer(64, _arena);

        IntPtr ptr2 = GetPointer(buf2.Span);
        IntPtr ptr3 = GetPointer(buf3.Span);

        // FIXED: No longer aliased
        Assert.NotEqual(ptr2, ptr3);

        buf2.Dispose();
        buf3.Dispose();
    }

    [Fact]
    public void SlabReallocation_IsNotMarkedAsFreed()
    {
        var buf1 = new SecureBuffer(64, _arena);
        buf1.Dispose();

        // Reallocate from slab
        var buf2 = new SecureBuffer(64, _arena);

        // FIXED: buf2 is not marked as disposed
        Assert.False(buf2.IsDisposed);

        buf2.Dispose();
    }

    [Fact]
    public void UseAfterFree_Throws()
    {
        var buf = new SecureBuffer(64, _arena);
        buf.Dispose();

        // Check IsDisposed flag instead of trying to access in lambda
        Assert.True(buf.IsDisposed);

        // Verify implicit operator throws
        try
        {
            Span<byte> span = buf;
            _ = span[0];
            Assert.Fail("Expected ObjectDisposedException");
        }
        catch (ObjectDisposedException)
        {
            // Expected
        }
    }

    [Fact]
    public void MemoryAliasing_DoesNotOccur()
    {
        // Create double-free scenario
        var buf1 = new SecureBuffer(32, _arena);
        buf1.Dispose();
        buf1.Dispose(); // Double free attempt

        // Allocate two new buffers
        var sessionA = new SecureBuffer(32, _arena);
        var sessionB = new SecureBuffer(32, _arena);

        // Write different patterns
        for (int i = 0; i < 32; i++)
        {
            sessionA.Span[i] = 0xAA;
            sessionB.Span[i] = 0xBB;
        }

        // FIXED: No memory aliasing
        Assert.NotEqual(sessionA.Span.ToArray(), sessionB.Span.ToArray());

        sessionA.Dispose();
        sessionB.Dispose();
    }

    [Fact]
    public void ConcurrentAllocations_NoCorruption()
    {
        const int iterations = 10000;
        var pointers = new System.Collections.Concurrent.ConcurrentBag<IntPtr>();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, iterations, _ =>
        {
            try
            {
                var buf = new SecureBuffer(32, _arena);
                IntPtr ptr = GetPointer(buf.Span);
                pointers.Add(ptr);

                // Write and verify pattern
                for (int i = 0; i < 32; i++)
                {
                    buf.Span[i] = (byte)(i & 0xFF);
                }

                for (int i = 0; i < 32; i++)
                {
                    if (buf.Span[i] != (byte)(i & 0xFF))
                    {
                        throw new InvalidOperationException("Memory corruption detected");
                    }
                }

                buf.Dispose();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        // Should have no errors
        Assert.Empty(errors);

        // Should have allocated many buffers
        Assert.Equal(iterations, pointers.Count);
    }

    private static IntPtr GetPointer(Span<byte> span)
    {
        unsafe
        {
            fixed (byte* p = span)
            {
                return (IntPtr)p;
            }
        }
    }
}

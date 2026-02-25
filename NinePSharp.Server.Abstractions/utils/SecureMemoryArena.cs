using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A contiguous, RAM-locked memory region using a slab-pool allocator.
/// Eliminates the race condition where a bump-allocator reset could wipe
/// memory still held by active <see cref="LuxVault"/> SecureBuffer consumers.
///
/// Design:
/// - The arena is divided into fixed-size slabs (32, 64, 256, 1024, 4096).
/// - Each slab size has its own ConcurrentStack of pre-carved pointers.
/// - Allocate pops from the appropriate stack; Free zeroes and pushes back.
/// - A separate overflow bump region handles odd sizes, refcounted.
/// - The arena NEVER globally wipes while allocations are active.
/// </summary>
public sealed class SecureMemoryArena : IDisposable
{
    private readonly IntPtr _baseAddress;
    private readonly int _totalSize;
    private int _disposed;

    // ── Slab pools ──────────────────────────────────────────────────────
    // Each pool manages pointers to pre-carved regions of fixed size.
    private static readonly int[] SlabSizes = [32, 64, 256, 1024, 4096];
    private readonly ConcurrentDictionary<int, ConcurrentStack<IntPtr>> _slabPools = new();
    private readonly int _slabRegionEnd; // byte offset where slab region ends

    // ── Overflow bump allocator ─────────────────────────────────────────
    // For sizes that don't match a slab. Refcounted to prevent wipe-under-use.
    private int _overflowOffset;
    private readonly int _overflowStart;
    private int _activeOverflowAllocations;
    private readonly object _overflowLock = new();

    // ── Global active allocation tracking ───────────────────────────────
    private int _activeAllocations;

    /// <summary>Number of currently outstanding (un-freed) allocations.</summary>
    public int ActiveAllocations => Volatile.Read(ref _activeAllocations);

    public SecureMemoryArena(int size = 1024 * 1024) // Default 1MB
    {
        _totalSize = size;
        _baseAddress = Marshal.AllocHGlobal(size);

        // Zero out and lock the entire region into RAM once
        unsafe { NativeMemory.Clear((void*)_baseAddress, (nuint)size); }

        if (!MemoryLock.Lock(_baseAddress, (nuint)size))
        {
            Marshal.FreeHGlobal(_baseAddress);
            throw new System.Security.SecurityException(
                "Failed to lock SecureMemoryArena into RAM. " +
                "Ensure OS limits allow memory locking (e.g. RLIMIT_MEMLOCK on Linux). " +
                "This is required for Zero-Exposure security.");
        }

        // Carve slab pools from the front of the arena
        int offset = 0;
        foreach (var slabSize in SlabSizes)
        {
            var stack = new ConcurrentStack<IntPtr>();
            // Allocate ~10% of total arena per slab size, minimum 4 slabs
            int slabCount = Math.Max(4, (size / 5) / slabSize / SlabSizes.Length);
            for (int i = 0; i < slabCount && offset + slabSize <= size; i++)
            {
                stack.Push(IntPtr.Add(_baseAddress, offset));
                offset += slabSize;
            }
            _slabPools[slabSize] = stack;
        }

        _slabRegionEnd = offset;
        _overflowStart = offset;
        _overflowOffset = offset;
    }

    /// <summary>
    /// Select the smallest slab size that fits the requested allocation.
    /// Returns -1 if no slab fits (will use overflow).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindSlabSize(int requestedSize)
    {
        foreach (var s in SlabSizes)
        {
            if (requestedSize <= s) return s;
        }
        return -1;
    }

    public unsafe Span<byte> Allocate(int size)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (size < 0 || size > _totalSize)
            throw new ArgumentOutOfRangeException(nameof(size),
                $"Allocation size must be between 0 and {_totalSize}.");

        if (size == 0) return new Span<byte>((void*)_baseAddress, 0);

        // Try slab pool first
        int slabSize = FindSlabSize(size);
        if (slabSize > 0 && _slabPools.TryGetValue(slabSize, out var stack) && stack.TryPop(out var ptr))
        {
            NativeMemory.Clear((void*)ptr, (nuint)slabSize);
            Interlocked.Increment(ref _activeAllocations);
            return new Span<byte>((void*)ptr, size);
        }

        // Fall back to overflow bump region (refcounted, never blindly wiped)
        lock (_overflowLock)
        {
            // If the overflow region is full AND no active overflow allocations,
            // we can safely reset it (all previous users have freed).
            if (_overflowOffset + size > _totalSize)
            {
                if (Volatile.Read(ref _activeOverflowAllocations) > 0)
                {
                    throw new OutOfMemoryException(
                        $"SecureMemoryArena overflow region exhausted ({_activeOverflowAllocations} active allocations). " +
                        "Cannot safely reset. Consider increasing arena size.");
                }

                // Safe to reset: zero and reset offset
                NativeMemory.Clear(
                    (void*)IntPtr.Add(_baseAddress, _overflowStart),
                    (nuint)(_totalSize - _overflowStart));
                _overflowOffset = _overflowStart;
            }

            IntPtr allocPtr = IntPtr.Add(_baseAddress, _overflowOffset);
            _overflowOffset += size;
            Interlocked.Increment(ref _activeOverflowAllocations);
            Interlocked.Increment(ref _activeAllocations);
            return new Span<byte>((void*)allocPtr, size);
        }
    }

    public unsafe void Free(Span<byte> slice)
    {
        if (slice.IsEmpty) return;

        fixed (byte* p = slice)
        {
            IntPtr ptr = (IntPtr)p;
            long ptrOffset = ptr.ToInt64() - _baseAddress.ToInt64();

            // Verify ptr is within arena bounds
            if (ptrOffset < 0 || ptrOffset >= _totalSize) return;

            // Zero the memory immediately on free
            NativeMemory.Clear(p, (nuint)slice.Length);

            // Determine if this was a slab allocation or overflow
            if (ptrOffset < _slabRegionEnd)
            {
                // Slab region: find matching slab size and return to pool
                int slabSize = FindSlabSize(slice.Length);
                if (slabSize > 0 && _slabPools.TryGetValue(slabSize, out var stack))
                {
                    stack.Push(ptr);
                }
            }
            else
            {
                // Overflow region: just decrement count (bump allocator can't reclaim individual blocks)
                Interlocked.Decrement(ref _activeOverflowAllocations);
            }

            Interlocked.Decrement(ref _activeAllocations);
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // Zero the entire arena before releasing
        unsafe { NativeMemory.Clear((void*)_baseAddress, (nuint)_totalSize); }

        MemoryLock.Unlock(_baseAddress, (nuint)_totalSize);
        Marshal.FreeHGlobal(_baseAddress);
    }
}

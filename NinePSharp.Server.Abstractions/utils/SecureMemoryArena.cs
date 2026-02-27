using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A contiguous, RAM-locked memory region using a slab-pool allocator.
/// Eliminates the race condition where a bump-allocator reset could wipe
/// memory still held by active SecureBuffer consumers.
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

    // ── Double-free prevention ──────────────────────────────────────────
    // Track freed pointers to prevent double-free attacks (legacy - being phased out)
    private readonly ConcurrentDictionary<IntPtr, byte> _freedPointers = new();

    // ── Allocation handle system (replaces pointer-based tracking) ──
    private long _nextHandleId;

    private struct AllocationMetadata
    {
        public IntPtr Pointer;
        public int OriginalSize;
        public bool IsFreed;
    }

    private readonly ConcurrentDictionary<long, AllocationMetadata> _allocations = new();

    /// <summary>Number of currently outstanding (un-freed) allocations.</summary>
    public int ActiveAllocations => Volatile.Read(ref _activeAllocations);

    /// <summary>
    /// Checks if a pointer has been freed (for double-free prevention).
    /// </summary>
    /// <param name="ptr">The pointer to check.</param>
    /// <returns>True if the pointer has been freed, false otherwise.</returns>
    public bool IsFreed(IntPtr ptr)
    {
        return _freedPointers.ContainsKey(ptr);
    }

    /// <summary>
    /// Checks if an allocation handle is still active (not freed).
    /// </summary>
    public bool IsAllocated(long handle)
    {
        return _allocations.TryGetValue(handle, out var meta) && !meta.IsFreed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureMemoryArena"/> class.
    /// </summary>
    /// <param name="size">The total size of the arena in bytes.</param>
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

    /// <summary>
    /// Allocates a RAM-locked buffer and returns an allocation handle for lifecycle tracking.
    /// </summary>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <param name="handle">Output handle for tracking this allocation.</param>
    /// <returns>A span pointing to the allocated memory.</returns>
    public unsafe Span<byte> Allocate(int size, out long handle)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (size < 0 || size > _totalSize)
            throw new ArgumentOutOfRangeException(nameof(size),
                $"Allocation size must be between 0 and {_totalSize}.");

        if (size == 0)
        {
            handle = 0;
            return new Span<byte>((void*)_baseAddress, 0);
        }

        // Generate unique handle FIRST
        handle = Interlocked.Increment(ref _nextHandleId);

        IntPtr allocPtr;

        // Try slab pool first
        int slabSize = FindSlabSize(size);
        if (slabSize > 0 && _slabPools.TryGetValue(slabSize, out var stack) && stack.TryPop(out allocPtr))
        {
            NativeMemory.Clear((void*)allocPtr, (nuint)slabSize);

            // CRITICAL: Remove from freed set if it was there
            _freedPointers.TryRemove(allocPtr, out _);

            // Track allocation
            _allocations[handle] = new AllocationMetadata
            {
                Pointer = allocPtr,
                OriginalSize = size,
                IsFreed = false
            };

            Interlocked.Increment(ref _activeAllocations);
            return new Span<byte>((void*)allocPtr, size);
        }

        // Fall back to overflow bump region
        lock (_overflowLock)
        {
            if (_overflowOffset + size > _totalSize)
            {
                if (Volatile.Read(ref _activeOverflowAllocations) > 0)
                {
                    throw new OutOfMemoryException(
                        $"SecureMemoryArena overflow region exhausted ({_activeOverflowAllocations} active). " +
                        "Cannot safely reset. Consider increasing arena size.");
                }

                NativeMemory.Clear(
                    (void*)IntPtr.Add(_baseAddress, _overflowStart),
                    (nuint)(_totalSize - _overflowStart));
                _overflowOffset = _overflowStart;
            }

            allocPtr = IntPtr.Add(_baseAddress, _overflowOffset);
            _overflowOffset += size;

            // Track allocation
            _allocations[handle] = new AllocationMetadata
            {
                Pointer = allocPtr,
                OriginalSize = size,
                IsFreed = false
            };

            Interlocked.Increment(ref _activeOverflowAllocations);
            Interlocked.Increment(ref _activeAllocations);
            return new Span<byte>((void*)allocPtr, size);
        }
    }

    /// <summary>
    /// Backward-compatible overload. Prefer Allocate(size, out handle) for new code.
    /// </summary>
    [Obsolete("Use Allocate(size, out handle) for proper lifecycle tracking")]
    public Span<byte> Allocate(int size)
    {
        return Allocate(size, out _);
    }

    /// <summary>
    /// Frees a previously allocated buffer back to the arena.
    /// </summary>
    /// <param name="slice">The span to free.</param>
    private unsafe void Free(Span<byte> slice)
    {
        // Legacy overload for backward compatibility - uses slice length (vulnerable to sliced-free)
        // This should only be called directly on arena, not through SecureBuffer
        Free(slice, slice.Length);
    }

    /// <summary>
    /// Frees a previously allocated buffer back to the arena using the original allocation size.
    /// SECURITY: This prevents sliced-free corruption by using originalSize instead of slice.Length.
    /// SECURITY: This prevents double-free by tracking freed pointers.
    /// </summary>
    /// <param name="slice">The span to free.</param>
    /// <param name="originalSize">The original allocation size (not the current slice length).</param>
    private unsafe void Free(Span<byte> slice, int originalSize)
    {
        if (slice.IsEmpty) return;

        fixed (byte* p = slice)
        {
            IntPtr ptr = (IntPtr)p;
            long ptrOffset = ptr.ToInt64() - _baseAddress.ToInt64();

            // Verify ptr is within arena bounds
            if (ptrOffset < 0 || ptrOffset >= _totalSize) return;

            // SECURITY FIX: Prevent double-free
            if (!_freedPointers.TryAdd(ptr, 0))
            {
                // Pointer already freed - this is a double-free attempt!
                return;
            }

            // [TESTING] Manual yield to encourage race detection without Coyote rewriting
            if (Environment.GetEnvironmentVariable("NINEPSHARP_TEST_RACE") == "1") Thread.Yield();

            // Zero the memory immediately on free (use slice.Length to only clear what we have)
            NativeMemory.Clear(p, (nuint)slice.Length);

            // Determine if this was a slab allocation or overflow
            if (ptrOffset < _slabRegionEnd)
            {
                // SECURITY FIX: Use originalSize, not slice.Length
                // This prevents sliced-free corruption where a 1024-byte allocation
                // sliced to 32 bytes would incorrectly return to the 32-byte pool
                int slabSize = FindSlabSize(originalSize);
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

    /// <summary>
    /// Frees an allocation by its handle. Provides atomic double-free protection and prevents metadata leaks.
    /// </summary>
    /// <param name="handle">The allocation handle to free.</param>
    public unsafe void Free(long handle)
    {
        if (handle == 0) return;

        // SECURITY FIX: TryRemove is atomic and prevents the metadata memory leak
        // It returns false if the handle was already removed (double-free prevention)
        if (!_allocations.TryRemove(handle, out var meta))
            return; 

        // Zero the memory using the original allocation size
        long ptrOffset = meta.Pointer.ToInt64() - _baseAddress.ToInt64();
        if (ptrOffset < 0 || ptrOffset >= _totalSize) return;

        NativeMemory.Clear((void*)meta.Pointer, (nuint)meta.OriginalSize);

        // Return to appropriate pool using ORIGINAL size
        if (ptrOffset < _slabRegionEnd)
        {
            int slabSize = FindSlabSize(meta.OriginalSize);
            if (slabSize > 0 && _slabPools.TryGetValue(slabSize, out var stack))
            {
                // Mark pointer as freed for legacy pointer-based checks
                _freedPointers.TryAdd(meta.Pointer, 0);
                stack.Push(meta.Pointer);
            }
        }
        else
        {
            lock (_overflowLock)
            {
                Interlocked.Decrement(ref _activeOverflowAllocations);
            }
        }

        Interlocked.Decrement(ref _activeAllocations);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SecureMemoryArena"/>, zeroing memory before release.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // Zero the entire arena before releasing
        unsafe { NativeMemory.Clear((void*)_baseAddress, (nuint)_totalSize); }

        MemoryLock.Unlock(_baseAddress, (nuint)_totalSize);
        Marshal.FreeHGlobal(_baseAddress);
    }
}

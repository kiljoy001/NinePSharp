using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A contiguous, RAM-locked memory region for secure sub-allocations.
/// Eliminates frequent mlock/munlock syscalls and GC pinning overhead.
/// </summary>
public sealed class SecureMemoryArena : IDisposable
{
    private readonly IntPtr _baseAddress;
    private readonly int _totalSize;
    private int _offset;
    private readonly object _lock = new();
    
    // Simple free list for reused blocks of common sizes (e.g. 32, 64, 256 bytes)
    private readonly ConcurrentDictionary<int, ConcurrentStack<IntPtr>> _freePool = new();

    public SecureMemoryArena(int size = 1024 * 1024) // Default 1MB
    {
        _totalSize = size;
        _baseAddress = Marshal.AllocHGlobal(size);
        
        // Zero out and lock the entire region into RAM once
        unsafe { NativeMemory.Clear((void*)_baseAddress, (nuint)size); }
        
        if (!MemoryLock.Lock(_baseAddress, (nuint)size))
        {
            throw new System.Security.SecurityException("Failed to lock SecureMemoryArena into RAM. Ensure OS limits allow memory locking (e.g. RLIMIT_MEMLOCK on Linux). This is required for Zero-Exposure security.");
        }
    }

    public unsafe Span<byte> Allocate(int size)
    {
        if (size > _totalSize) throw new ArgumentException("Allocation size exceeds arena capacity.");

        // Check pool first
        if (_freePool.TryGetValue(size, out var stack) && stack.TryPop(out var pooledPtr))
        {
            NativeMemory.Clear((void*)pooledPtr, (nuint)size);
            return new Span<byte>((void*)pooledPtr, size);
        }

        lock (_lock)
        {
            if (_offset + size > _totalSize)
            {
                // Simple arena strategy: reset if full (this assumes all short-lived secrets are done)
                // In a production scenario, we'd use a more complex allocator or multiple pages.
                // For NinePSharp's current use case, we reset and zero.
                _offset = 0;
                NativeMemory.Clear((void*)_baseAddress, (nuint)_totalSize);
            }

            IntPtr ptr = IntPtr.Add(_baseAddress, _offset);
            _offset += size;
            return new Span<byte>((void*)ptr, size);
        }
    }

    public unsafe void Free(Span<byte> slice)
    {
        fixed (byte* p = slice)
        {
            IntPtr ptr = (IntPtr)p;
            // Verify ptr is within arena
            if (ptr.ToInt64() >= _baseAddress.ToInt64() && ptr.ToInt64() < _baseAddress.ToInt64() + _totalSize)
            {
                NativeMemory.Clear(p, (nuint)slice.Length);
                var stack = _freePool.GetOrAdd(slice.Length, _ => new ConcurrentStack<IntPtr>());
                stack.Push(ptr);
            }
        }
    }

    public void Dispose()
    {
        MemoryLock.Unlock(_baseAddress, (nuint)_totalSize);
        Marshal.FreeHGlobal(_baseAddress);
    }
}

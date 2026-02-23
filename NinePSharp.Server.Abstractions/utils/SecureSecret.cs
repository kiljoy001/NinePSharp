using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A scope-bound, zero-wipe-on-dispose wrapper for decrypted secret material.
/// The backing array is pinned on the POH and mlock'd into RAM.
/// Callers MUST use this in a `using` block to guarantee cleanup.
/// </summary>
public sealed class SecureSecret : IDisposable
{
    private byte[]? _data;
    private int _disposed; // 0 = alive, 1 = disposed (interlocked for thread safety)

    public SecureSecret(byte[] pinnedLockedData)
    {
        _data = pinnedLockedData ?? throw new ArgumentNullException(nameof(pinnedLockedData));
    }

    /// <summary>
    /// Access the secret bytes. Returns default (empty) span if already disposed.
    /// </summary>
    public ReadOnlySpan<byte> Span =>
        Volatile.Read(ref _disposed) == 0 && _data != null
            ? _data.AsSpan()
            : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Length of the secret data, or 0 if disposed.
    /// </summary>
    public int Length => Volatile.Read(ref _disposed) == 0 ? (_data?.Length ?? 0) : 0;

    /// <summary>
    /// Whether this secret has been disposed (data wiped).
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Copy secret bytes to a destination span. Throws if disposed.
    /// </summary>
    public void CopyTo(Span<byte> destination)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        Span.CopyTo(destination);
    }

    /// <summary>
    /// Execute an action with the secret bytes, ensuring the data is available for the duration.
    /// </summary>
    public T Use<T>(Func<ReadOnlySpan<byte>, T> action)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        return action(Span);
    }

    /// <summary>
    /// Execute an action with the secret bytes.
    /// </summary>
    public void Use(Action<ReadOnlySpan<byte>> action)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        action(Span);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        var data = _data;
        _data = null;

        if (data != null)
        {
            // 1. Zero the secret material
            Array.Clear(data);

            // 2. Unlock from RAM (paired with Lock in DecryptInternal)
            unsafe
            {
                fixed (byte* p = data)
                {
                    MemoryLock.Unlock((IntPtr)p, (nuint)data.Length);
                }
            }

            // The array itself (on the POH) will be collected by GC,
            // but its contents are now zeroed and unlocked.
        }
    }
}

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

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureSecret"/> class with pinned, locked data.
    /// </summary>
    /// <param name="pinnedLockedData">The pinned and mlock'd byte array containing the secret.</param>
    public SecureSecret(byte[] pinnedLockedData)
    {
        _data = pinnedLockedData ?? throw new ArgumentNullException(nameof(pinnedLockedData));
    }

    /// <summary>
    /// Gets the secret bytes as a <see cref="ReadOnlySpan{Byte}"/>. 
    /// Returns default (empty) span if already disposed.
    /// </summary>
    public ReadOnlySpan<byte> Span =>
        Volatile.Read(ref _disposed) == 0 && _data != null
            ? _data.AsSpan()
            : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Gets the length of the secret data, or 0 if disposed.
    /// </summary>
    public int Length => Volatile.Read(ref _disposed) == 0 ? (_data?.Length ?? 0) : 0;

    /// <summary>
    /// Gets a value indicating whether this secret has been disposed (data wiped).
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Copy secret bytes to a destination span. Throws if disposed.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the secret is already disposed.</exception>
    public void CopyTo(Span<byte> destination)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        Span.CopyTo(destination);
    }

    /// <summary>
    /// Execute an action with the secret bytes, ensuring the data is available for the duration.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The action to perform with the secret bytes.</param>
    /// <returns>The result of the action.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the secret is already disposed.</exception>
    public T Use<T>(Func<ReadOnlySpan<byte>, T> action)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        return action(Span);
    }

    /// <summary>
    /// Execute an action with the secret bytes.
    /// </summary>
    /// <param name="action">The action to perform with the secret bytes.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the secret is already disposed.</exception>
    public void Use(Action<ReadOnlySpan<byte>> action)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SecureSecret));
        action(Span);
    }

    /// <summary>
    /// Wipes the secret material from memory, unlocks the memory page, and marks the instance as disposed.
    /// </summary>
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

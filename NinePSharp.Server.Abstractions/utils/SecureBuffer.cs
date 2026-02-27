using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A scope-bound buffer allocated from a RAM-locked SecureMemoryArena.
/// SECURITY: Uses allocation handles to prevent double-free and use-after-free.
/// </summary>
public ref struct SecureBuffer
{
    private readonly SecureMemoryArena? _arena;
    private readonly long _handle;

    /// <summary>Gets the memory span for this buffer.</summary>
    public Span<byte> Span { get; }

    /// <summary>Gets the length of the buffer.</summary>
    public int Length => Span.Length;

    /// <summary>Gets whether this buffer has been disposed.</summary>
    public bool IsDisposed => _arena == null || !_arena.IsAllocated(_handle);

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureBuffer"/> struct.
    /// </summary>
    /// <param name="size">Size in bytes.</param>
    /// <param name="arena">The arena to allocate from.</param>
    public SecureBuffer(int size, SecureMemoryArena arena)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        Span = arena.Allocate(size, out _handle);
    }

    /// <summary>
    /// Returns the buffer memory to the arena.
    /// SECURITY: Double-free safe via handle tracking.
    /// </summary>
    public void Dispose()
    {
        if (_arena == null) return;
        _arena.Free(_handle);
    }

    /// <summary>Converts the buffer to a span.</summary>
    /// <exception cref="ObjectDisposedException">Thrown if the buffer has been disposed.</exception>
    public static implicit operator Span<byte>(SecureBuffer b)
    {
        if (b.IsDisposed)
            throw new ObjectDisposedException(nameof(SecureBuffer), "Cannot access a disposed SecureBuffer.");
        return b.Span;
    }

    /// <summary>Converts the buffer to a read-only span.</summary>
    /// <exception cref="ObjectDisposedException">Thrown if the buffer has been disposed.</exception>
    public static implicit operator ReadOnlySpan<byte>(SecureBuffer b)
    {
        if (b.IsDisposed)
            throw new ObjectDisposedException(nameof(SecureBuffer), "Cannot access a disposed SecureBuffer.");
        return b.Span;
    }
}

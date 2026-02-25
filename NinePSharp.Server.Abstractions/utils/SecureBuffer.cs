using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A scope-bound buffer allocated from a RAM-locked SecureMemoryArena.
/// </summary>
public ref struct SecureBuffer
{
    private readonly SecureMemoryArena _arena;
    
    /// <summary>Gets the memory span for this buffer.</summary>
    public Span<byte> Span { get; }
    
    /// <summary>Gets the length of the buffer.</summary>
    public int Length => Span.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureBuffer"/> struct.
    /// </summary>
    /// <param name="size">Size in bytes.</param>
    /// <param name="arena">The arena to allocate from.</param>
    public SecureBuffer(int size, SecureMemoryArena arena)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        Span = arena.Allocate(size);
    }

    /// <summary>
    /// Returns the buffer memory to the arena.
    /// </summary>
    public void Dispose()
    {
        _arena.Free(Span);
    }

    /// <summary>Converts the buffer to a span.</summary>
    public static implicit operator Span<byte>(SecureBuffer b) => b.Span;
    /// <summary>Converts the buffer to a read-only span.</summary>
    public static implicit operator ReadOnlySpan<byte>(SecureBuffer b) => b.Span;
}

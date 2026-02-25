using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// A scope-bound buffer allocated from a RAM-locked SecureMemoryArena.
/// </summary>
public ref struct SecureBuffer
{
    private readonly SecureMemoryArena _arena;
    public Span<byte> Span { get; }
    public int Length => Span.Length;

    public SecureBuffer(int size, SecureMemoryArena arena)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        Span = arena.Allocate(size);
    }

    public void Dispose()
    {
        _arena.Free(Span);
    }

    public static implicit operator Span<byte>(SecureBuffer b) => b.Span;
    public static implicit operator ReadOnlySpan<byte>(SecureBuffer b) => b.Span;
}

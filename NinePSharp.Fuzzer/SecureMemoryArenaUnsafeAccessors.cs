using System;
using System.Runtime.CompilerServices;
using NinePSharp.Server.Utils;

namespace NinePSharp.Fuzzer;

internal static class SecureMemoryArenaUnsafeAccessors
{
    // Fuzzer-only hook for the legacy private span-based free path.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Free")]
    internal static extern void FreeSlice(SecureMemoryArena arena, Span<byte> slice);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Free")]
    internal static extern void FreeSlice(SecureMemoryArena arena, Span<byte> slice, int originalSize);
}

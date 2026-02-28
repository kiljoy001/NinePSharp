using System;
using System.Runtime.CompilerServices;
using NinePSharp.Server.Utils;

namespace NinePSharp.Tests.Helpers;

internal static class SecureMemoryArenaUnsafeAccessors
{
    // Test-only hook for regression coverage of the legacy private span-based free path.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Free")]
    internal static extern void FreeSlice(SecureMemoryArena arena, Span<byte> slice);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Free")]
    internal static extern void FreeSlice(SecureMemoryArena arena, Span<byte> slice, int originalSize);
}

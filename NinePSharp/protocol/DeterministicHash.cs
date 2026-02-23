using System;
using System.IO.Hashing;
using System.Text;

namespace NinePSharp.Protocol;

public static class DeterministicHash
{
    /// <summary>
    /// Computes a stable 64-bit XxHash64 of a string.
    /// This is deterministic across runs and platforms, and uses Microsoft's
    /// optimized implementation from System.IO.Hashing.
    /// </summary>
    public static ulong GetStableHash64(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return XxHash64.HashToUInt64(bytes);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Helper methods for working with <see cref="SecureString"/>.
/// </summary>
public static class SecureStringHelper
{
    /// <summary>
    /// Temporarily converts a SecureString to a managed string.
    /// WARNING: This leaks the secret into the managed heap. 
    /// Use only when third-party libraries require a string and cannot accept bytes/spans.
    /// </summary>
    /// <param name="ss">The SecureString to convert.</param>
    /// <returns>A managed string containing the secret.</returns>
    [Obsolete("Use the Use() method to prevent leakage into the managed string pool.")]
    public static string ToString(SecureString ss)
    {
        if (ss == null) throw new ArgumentNullException(nameof(ss));
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(ss);
        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    /// <summary>
    /// Decodes a SecureString into a temporary UTF-8 buffer and executes an action.
    /// The buffer is explicitly zeroed out after the action completes.
    /// </summary>
    public static void Use(SecureString ss, Action<ReadOnlySpan<byte>> action)
    {
        Use<int>(ss, span => { action(span); return 0; });
    }

    /// <summary>
    /// Decodes a SecureString into a temporary UTF-8 buffer and executes a function.
    /// The buffer is explicitly zeroed out after the function completes.
    /// </summary>
    public static T Use<T>(SecureString ss, Func<ReadOnlySpan<byte>, T> action)
    {
        if (ss == null) throw new ArgumentNullException(nameof(ss));
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(ss);
        nuint unmanagedLen = (nuint)(ss.Length * 2);
        MemoryLock.Lock(ptr, unmanagedLen);
        
        try
        {
            unsafe {
                char* chars = (char*)ptr.ToPointer();
                int length = ss.Length;
                int byteCount = System.Text.Encoding.UTF8.GetByteCount(chars, length);
                byte[] bytes = GC.AllocateArray<byte>(byteCount, pinned: true);
                
                fixed (byte* pBytes = bytes) {
                    MemoryLock.Lock((IntPtr)pBytes, (nuint)bytes.Length);
                }

                try {
                    fixed (byte* pBytes = bytes) {
                        System.Text.Encoding.UTF8.GetBytes(chars, length, pBytes, byteCount);
                        return action(new ReadOnlySpan<byte>(pBytes, byteCount));
                    }
                }
                finally {
                    fixed (byte* pBytes = bytes) {
                        MemoryLock.Unlock((IntPtr)pBytes, (nuint)bytes.Length);
                    }
                    Array.Clear(bytes);
                }
            }
        }
        finally
        {
            MemoryLock.Unlock(ptr, unmanagedLen);
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}

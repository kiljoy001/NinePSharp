using System;
using System.Runtime.InteropServices;
using System.Security;

namespace NinePSharp.Server.Utils;

public static class SecureStringHelper
{
    /// <summary>
    /// Temporarily converts a SecureString to a managed string.
    /// WARNING: This leaks the secret into the managed heap. 
    /// Use only when third-party libraries require a string and cannot accept bytes/spans.
    /// </summary>
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
}

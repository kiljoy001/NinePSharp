using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Provides cross-platform memory locking capabilities.
/// </summary>
public static class MemoryLock
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Locks the specified memory region into RAM, preventing it from being swapped to disk.
    /// </summary>
    public static bool Lock(IntPtr address, nuint length)
    {
        if (address == IntPtr.Zero || length == 0) return true;

        try
        {
            if (IsWindows)
            {
                return NativeMethods.VirtualLock(address, length);
            }
            if (IsLinux)
            {
                return NativeMethods.mlock(address, length) == 0;
            }
        }
        catch
        {
            // Fallback for unsupported platforms or errors
        }

        return false;
    }

    /// <summary>
    /// Unlocks the specified memory region.
    /// </summary>
    public static bool Unlock(IntPtr address, nuint length)
    {
        if (address == IntPtr.Zero || length == 0) return true;

        try
        {
            if (IsWindows)
            {
                return NativeMethods.VirtualUnlock(address, length);
            }
            if (IsLinux)
            {
                return NativeMethods.munlock(address, length) == 0;
            }
        }
        catch
        {
            // Fallback
        }

        return false;
    }
}

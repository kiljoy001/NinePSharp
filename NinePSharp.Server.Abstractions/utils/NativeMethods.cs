using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils;

/// <summary>
/// P/Invoke definitions for platform-native methods.
/// </summary>
public static partial class NativeMethods
{
    private const string LibC = "libc";
    private const string Kernel32 = "kernel32.dll";

    // --- Linux / POSIX ---

    /// <summary>Locks a region of memory into RAM (Linux).</summary>
    [LibraryImport(LibC, SetLastError = true)]
    public static partial int mlock(IntPtr addr, nuint len);

    /// <summary>Unlocks a region of memory (Linux).</summary>
    [LibraryImport(LibC, SetLastError = true)]
    public static partial int munlock(IntPtr addr, nuint len);

    // --- Windows ---

    /// <summary>Locks a region of memory into RAM (Windows).</summary>
    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualLock(IntPtr lpAddress, nuint dwSize);

    /// <summary>Unlocks a region of memory (Windows).</summary>
    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualUnlock(IntPtr lpAddress, nuint dwSize);

    // --- Linux Process Control ---

    /// <summary>Option for prctl to set dumpable state.</summary>
    public const int PR_SET_DUMPABLE = 4;

    /// <summary>Process control operations (Linux).</summary>
    [LibraryImport(LibC, SetLastError = true)]
    public static partial int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);
}

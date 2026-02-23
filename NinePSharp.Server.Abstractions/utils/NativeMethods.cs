using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils;

public static partial class NativeMethods
{
    private const string LibC = "libc";
    private const string Kernel32 = "kernel32.dll";

    // --- Linux / POSIX ---

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int mlock(IntPtr addr, nuint len);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int munlock(IntPtr addr, nuint len);

    // --- Windows ---

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualLock(IntPtr lpAddress, nuint dwSize);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualUnlock(IntPtr lpAddress, nuint dwSize);

    // --- Linux Process Control ---

    public const int PR_SET_DUMPABLE = 4;

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);
}

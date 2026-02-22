using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils;

public static class ProcessHardening
{
    /// <summary>
    /// Applies OS-level protections to the current process to prevent secret leakage.
    /// </summary>
    public static void Apply()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Disable core dumps and ptrace attaching for non-root users
            int result = NativeMethods.prctl(NativeMethods.PR_SET_DUMPABLE, 0, 0, 0, 0);
            if (result != 0)
            {
                Console.WriteLine("[WARNING] Failed to set PR_SET_DUMPABLE. Process may be dumpable.");
            }
            else
            {
                Console.WriteLine("[INFO] Anti-Dumping protection enabled (PR_SET_DUMPABLE=0).");
            }
        }
        
        // Windows: SetProcessMitigationPolicy could be used here for advanced hardening
    }
}

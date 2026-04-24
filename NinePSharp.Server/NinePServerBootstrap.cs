using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace NinePSharp.Server;

public static class NinePServerBootstrap
{
    /// <summary>
    /// Basic server initialization. Security-specific hardening has been removed
    /// to maintain a lean, protocol-focused library.
    /// </summary>
    public static void Initialize()
    {
        // Add any necessary basic initialization here.
    }
}

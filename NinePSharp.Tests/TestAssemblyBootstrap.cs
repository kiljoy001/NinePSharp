using NinePSharp.Constants;
using System.Runtime.CompilerServices;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;

namespace NinePSharp.Tests;

internal static class TestAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ProcessHardening.Apply();
        byte[] sessionKey = new byte[32];
        for (int i = 0; i < sessionKey.Length; i++)
        {
            sessionKey[i] = (byte)i;
        }

        LuxVault.InitializeSessionKey(sessionKey);
        ProtectedSecret.InitializeSessionKey(sessionKey);

        // Keep test encryption/decryption runtime stable across classes.
        LuxVault.Iterations = 10000;
    }
}

using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server;

public static class NinePServerBootstrap
{
    public static void ApplyProcessHardening() => ProcessHardening.Apply();

    public static void CleanupVaults() => LuxVault.CleanupVaults();

    public static void CleanupVaultsOnStartup() => CleanupVaults();

    public static void CleanupVaultsOnShutdown() => CleanupVaults();

    public static SecureString Generate4096BitSecureSeed()
    {
        byte[] seedBytes = new byte[512];
        RandomNumberGenerator.Fill(seedBytes);

        var secure = new SecureString();
        foreach (var b in seedBytes)
        {
            secure.AppendChar((char)b);
        }

        secure.MakeReadOnly();
        Array.Clear(seedBytes);
        return secure;
    }

    public static byte[] DeriveSessionKeyFromSecureSeed(SecureString secureSeed)
    {
        ArgumentNullException.ThrowIfNull(secureSeed);

        byte[] hashedKey = new byte[32];
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureSeed);
        try
        {
            unsafe
            {
                byte* pSeed = (byte*)ptr.ToPointer();
                byte[] seedBytes = new byte[secureSeed.Length];
                for (int i = 0; i < secureSeed.Length; i++)
                {
                    seedBytes[i] = pSeed[i * 2];
                }

                MonocypherNative.crypto_blake2b(hashedKey, (nuint)hashedKey.Length, seedBytes, (nuint)seedBytes.Length);
                Array.Clear(seedBytes);
            }
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }

        return hashedKey;
    }

    public static void InitializeTransientSessionSecrets()
    {
        CleanupVaultsOnStartup();

        using SecureString secureSeed = Generate4096BitSecureSeed();
        byte[] sessionKey = DeriveSessionKeyFromSecureSeed(secureSeed);
        try
        {
            InitializeTransientSessionSecrets(sessionKey);
        }
        finally
        {
            Array.Clear(sessionKey);
        }
    }

    public static void InitializeTransientSessionSecrets(ReadOnlySpan<byte> sessionKey)
    {
        LuxVault.InitializeSessionKey(sessionKey);
        ProtectedSecret.InitializeSessionKey(sessionKey);
    }
}

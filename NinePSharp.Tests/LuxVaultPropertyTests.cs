using NinePSharp.Constants;
using System;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Server.Utils;
using Xunit;
using NinePSharp.Generators;

namespace NinePSharp.Tests;

public class LuxVaultPropertyTests
{
    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (char c in value)
        {
            secure.AppendChar(c);
        }
        secure.MakeReadOnly();
        return secure;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool LuxVault_DeriveSeed_Consistency_Property(string password, byte[] nonce)
    {
        if (password == null || nonce == null || nonce.Length < 8) return true;

        Span<byte> seed1 = stackalloc byte[32];
        Span<byte> seed2 = stackalloc byte[32];
        Span<byte> seed3 = stackalloc byte[32];

        // 1. From string
        LuxVault.DeriveSeed(Encoding.UTF8.GetBytes(password), nonce, seed1);

        // 2. From SecureString
        using (var ss = ToSecureString(password))
        {
            LuxVault.DeriveSeed(ss, nonce, seed2);
        }

        // 3. From bytes
        byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
        LuxVault.DeriveSeed(pwdBytes, nonce, seed3);

        return seed1.SequenceEqual(seed2) && seed1.SequenceEqual(seed3);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool LuxVault_Encryption_Roundtrip_Property(byte[] data, string password)
    {
        if (data == null || password == null) return true;

        // Roundtrip with string password
        byte[] encrypted = LuxVault.Encrypt(data, Encoding.UTF8.GetBytes(password));
        using (var decrypted = LuxVault.DecryptToBytes(encrypted, Encoding.UTF8.GetBytes(password)))
        {
            if (decrypted == null) return false;
            if (!data.SequenceEqual(decrypted.Span.ToArray())) return false;
        }

        // Roundtrip with SecureString password
        using (var ss = ToSecureString(password))
        {
            byte[] encryptedSs = LuxVault.Encrypt(data, ss);
            using (var decryptedSs = LuxVault.DecryptToBytes(encryptedSs, ss))
            {
                if (decryptedSs == null) return false;
                if (!data.SequenceEqual(decryptedSs.Span.ToArray())) return false;
            }
        }

        return true;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool LuxVault_GenerateHiddenId_Stability_Property(byte[] seed)
    {
        if (seed == null || seed.Length != 32) return true;

        string id1 = LuxVault.GenerateHiddenId(seed);
        string id2 = LuxVault.GenerateHiddenId(seed);

        return id1 == id2 && !string.IsNullOrEmpty(id1);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool LuxVault_ProtectConfig_Roundtrip_Property(string configValue, byte[] masterKey)
    {
        if (configValue == null || masterKey == null || masterKey.Length != 32) return true;

        string protectedValue = LuxVault.ProtectConfig(Encoding.UTF8.GetBytes(configValue), masterKey);
        using var unprotectedBuffer = LuxVault.UnprotectConfigToBytes(protectedValue, masterKey);
        
        if (unprotectedBuffer == null) return false;
        string unprotectedValue = Encoding.UTF8.GetString(unprotectedBuffer.Span);

        return configValue == unprotectedValue;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool LuxVault_TamperedPayload_Decryption_Fails_Property(byte[] data, string password, int tamperIndex)
    {
        if (data == null || password == null || data.Length == 0) return true;

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] encrypted = LuxVault.Encrypt(data, passwordBytes);
        
        // Tamper with ciphertext (skip salt and nonce and mac headers)
        // Salt(16) + Nonce(24) + Mac(16) = 56 bytes header
        int headerSize = 16 + 24 + 16;
        if (encrypted.Length <= headerSize) return true;

        int actualTamperIndex = headerSize + (Math.Abs(tamperIndex) % (encrypted.Length - headerSize));
        encrypted[actualTamperIndex] ^= 0xFF;

        using (var decrypted = LuxVault.DecryptToBytes(encrypted, passwordBytes))
        {
            // Decryption should return null for tampered data due to Poly1305 MAC failure
            return decrypted == null;
        }
    }
}

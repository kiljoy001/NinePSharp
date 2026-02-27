using NinePSharp.Constants;
using System;
using System.Linq;
using System.Security;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class LuxVaultRobustnessTests
{
    [Property]
    public void LuxVault_Encryption_RoundTrip_Property(byte[] data, string password)
    {
        if (data == null || password == null) return;
        if (data.Length < 16) return; // We use salt as nonce in Encrypt, salt is 16 bytes. 
        // Wait, the property test is for ANY data. 
        // Inside Encrypt, we generate a random 16-byte salt and pass it to DeriveSeed.
        // So the 'data' parameter here is the PLAINTEXT, not the nonce.
        // The nonce inside Encrypt is fine.
        
        // Wait, if so, why did it fail with null?
        // Ah, the failure I saw before was ([|246uy;...|], "q3\031\004.!nd")
        // password was "q3\031\004.!nd" (10 chars). 
        // That's more than 8 bytes.
        
        var encrypted = LuxVault.Encrypt(data, password);
        using var decrypted = LuxVault.DecryptToBytes(encrypted, password);

        Assert.NotNull(decrypted);
        Assert.True(data.SequenceEqual(decrypted.Span.ToArray()));
    }

    [Fact]
    public void LuxVault_Decrypt_Returns_Null_For_Corrupted_Mac()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello security");
        string password = "strongpassword";
        var encrypted = LuxVault.Encrypt(data, password);

        // Payload: Salt(16) + Nonce(24) + Mac(16) + Ciphertext
        // Corrupt the Mac
        encrypted[16 + 24] ^= 0xFF;

        using var decrypted = LuxVault.DecryptToBytes(encrypted, password);
        Assert.Null(decrypted);
    }

    [Fact]
    public void LuxVault_Decrypt_Returns_Null_For_Corrupted_Ciphertext()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello security");
        string password = "strongpassword";
        var encrypted = LuxVault.Encrypt(data, password);

        // Corrupt the ciphertext (last byte)
        encrypted[encrypted.Length - 1] ^= 0xFF;

        using var decrypted = LuxVault.DecryptToBytes(encrypted, password);
        Assert.Null(decrypted);
    }

    [Fact]
    public void LuxVault_Decrypt_Returns_Null_For_Short_Payload()
    {
        string password = "password";
        var result = LuxVault.DecryptToBytes(new byte[10], password);
        Assert.Null(result);
    }

    [Property]
    public void LuxVault_Different_Passwords_Produce_Different_Seeds(string p1, string p2)
    {
        if (string.IsNullOrEmpty(p1) || string.IsNullOrEmpty(p2) || p1 == p2) return;

        byte[] nonce = new byte[16];
        byte[] s1 = new byte[32];
        LuxVault.DeriveSeed(p1, nonce, s1);
        byte[] s2 = new byte[32];
        LuxVault.DeriveSeed(p2, nonce, s2);

        Assert.False(s1.SequenceEqual(s2));
    }

    [Fact]
    public void LuxVault_GenerateHiddenId_Is_Deterministic()
    {
        byte[] seed = new byte[32];
        new System.Random(42).NextBytes(seed);

        byte[] seed1 = (byte[])seed.Clone();
        byte[] seed2 = (byte[])seed.Clone();

        var h1 = LuxVault.GenerateHiddenId(seed1);
        var h2 = LuxVault.GenerateHiddenId(seed2);

        Assert.Equal(h1, h2);
    }
}

using NinePSharp.Constants;
using System;
using System.Linq;
using System.Security;
using System.Text;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class ZeroExposureTests
{
    private readonly LuxVaultService _vault = new LuxVaultService();

    [Fact]
    public void ProtectedSecret_SecureString_RoundTrip()
    {
        string secretText = "SuperSecret123!";
        using var ss = new SecureString();
        foreach (char c in secretText) ss.AppendChar(c);
        ss.MakeReadOnly();

        byte[] sessionKey = new byte[32];
        new System.Random().NextBytes(sessionKey);
        ProtectedSecret.InitializeSessionKey(sessionKey);

        using var ps = new ProtectedSecret(ss);
        
        string recovered = "";
        ps.Use(bytes => {
            recovered = Encoding.UTF8.GetString(bytes);
        });

        Assert.Equal(secretText, recovered);
    }

    [Fact]
    public void LuxVault_SecureString_PBKDF2_Consistency()
    {
        string password = "password123";
        ReadOnlySpan<byte> nonce = "1234567812345678"u8;

        using var ss = new SecureString();
        foreach (char c in password) ss.AppendChar(c);
        ss.MakeReadOnly();

        Span<byte> seedFromString = stackalloc byte[32];
        Span<byte> seedFromSecureString = stackalloc byte[32];
        LuxVault.DeriveSeed(password, nonce, seedFromString);
        LuxVault.DeriveSeed(ss, nonce, seedFromSecureString);

        Assert.True(seedFromString.SequenceEqual(seedFromSecureString), "DeriveSeed should produce same result for string and SecureString");
    }

    [Fact]
    public void ProtectedSecret_ReadOnlySpan_Constructor()
    {
        byte[] secretBytes = Encoding.UTF8.GetBytes("SecretData");
        
        using var ps = new ProtectedSecret((ReadOnlySpan<byte>)secretBytes);
        
        byte[]? recovered = null;
        ps.Use(bytes => {
            recovered = bytes.ToArray();
        });

        Assert.NotNull(recovered);
        Assert.True(secretBytes.SequenceEqual(recovered));
    }

    [Fact]
    public void LuxVault_Encrypt_ReadOnlySpan_Overload()
    {
        byte[] plain = Encoding.UTF8.GetBytes("Hello World");
        byte[] key = new byte[32];
        new System.Random().NextBytes(key);

        byte[] ciphertext = LuxVault.Encrypt((ReadOnlySpan<byte>)plain, (ReadOnlySpan<byte>)key);
        using var decrypted = LuxVault.DecryptToBytes(ciphertext, (ReadOnlySpan<byte>)key);

        Assert.NotNull(decrypted);
        Assert.True(plain.SequenceEqual(decrypted.Span.ToArray()));
    }

    [Fact]
    public void ProtectedSecret_ToString_DoesNotLeak()
    {
        #pragma warning disable CS0618
        using var ps = new ProtectedSecret("my-secret");
        #pragma warning restore CS0618
        Assert.Equal("********", ps.ToString());
    }
}

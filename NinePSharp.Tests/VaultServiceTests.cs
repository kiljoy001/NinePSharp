using NinePSharp.Constants;
using System;
using System.Text;
using Xunit;
using NinePSharp.Server.Utils;

namespace NinePSharp.Tests.Utils;

public class VaultServiceTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrip_Works()
    {
        string plaintext = "0x4c08835244d2c1ae355e155bc7023363b9d033501cb5ce57659546fc4dcc07bb";
        string password = "strong-password-123";

        var payload = VaultService.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(password));
        Assert.NotNull(payload.Salt);
        Assert.NotNull(payload.Nonce);
        Assert.NotNull(payload.Ciphertext);
        Assert.NotNull(payload.Tag);

        using var decrypted = VaultService.DecryptToBytes(payload, Encoding.UTF8.GetBytes(password));
        Assert.Equal(plaintext, Encoding.UTF8.GetString(decrypted.Span));
    }

    [Fact]
    public void Decrypt_WithWrongPassword_Throws()
    {
        string plaintext = "secret";
        string password = "password";
        var payload = VaultService.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(password));

        Assert.ThrowsAny<Exception>(() => {
            using var secret = VaultService.DecryptToBytes(payload, Encoding.UTF8.GetBytes("wrong-password"));
        });
    }

    [Fact]
    public void Base64_Serialization_Works()
    {
        string plaintext = "secret";
        string password = "password";
        var payload = VaultService.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(password));
        
        byte[] bytes = payload.ToBytes();
        var reconstructed = VaultService.EncryptedPayload.FromBytes(bytes);
        
        using var decrypted = VaultService.DecryptToBytes(reconstructed, Encoding.UTF8.GetBytes(password));
        Assert.Equal(plaintext, Encoding.UTF8.GetString(decrypted.Span));
    }
}

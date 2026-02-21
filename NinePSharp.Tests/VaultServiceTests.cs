using System;
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

        var payload = VaultService.Encrypt(plaintext, password);
        Assert.NotNull(payload.Salt);
        Assert.NotNull(payload.Nonce);
        Assert.NotNull(payload.Ciphertext);
        Assert.NotNull(payload.Tag);

        string decrypted = VaultService.Decrypt(payload, password);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WithWrongPassword_Throws()
    {
        string plaintext = "secret";
        string password = "password";
        var payload = VaultService.Encrypt(plaintext, password);

        Assert.ThrowsAny<Exception>(() => VaultService.Decrypt(payload, "wrong-password"));
    }

    [Fact]
    public void Base64_Serialization_Works()
    {
        string plaintext = "secret";
        string password = "password";
        var payload = VaultService.Encrypt(plaintext, password);
        
        byte[] bytes = payload.ToBytes();
        var reconstructed = VaultService.EncryptedPayload.FromBytes(bytes);
        
        string decrypted = VaultService.Decrypt(reconstructed, password);
        Assert.Equal(plaintext, decrypted);
    }
}

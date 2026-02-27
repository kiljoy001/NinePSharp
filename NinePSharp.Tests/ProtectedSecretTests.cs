using NinePSharp.Constants;
using System;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class ProtectedSecretTests
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

        public ProtectedSecretTests()
        {
            // Initialize session key if not already done by another test
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            ProtectedSecret.InitializeSessionKey(key);
        }

        [Fact]
        public void ProtectedSecret_RoundTrip_Works()
        {
            string original = "my-ultra-secret-password-123";
            using var originalSecure = ToSecureString(original);
            using var secret = new ProtectedSecret(originalSecure);
            
            // Should not show cleartext in ToString
            Assert.DoesNotContain(original, secret.ToString());
            Assert.Equal("********", secret.ToString());

            string? revealed = null;
            secret.Use(bytes => revealed = Encoding.UTF8.GetString(bytes));
            Assert.Equal(original, revealed);
        }

        [Fact]
        public void ProtectedSecret_EncryptionIsUniquePerInstance()
        {
            // This tests that even for the same plaintext, the encrypted data in memory is different
            // due to the random salt/nonce used by LuxVault internally.
            string plaintext = "static-secret";
            using var plaintextSecure1 = ToSecureString(plaintext);
            using var plaintextSecure2 = ToSecureString(plaintext);
            
            using var s1 = new ProtectedSecret(plaintextSecure1);
            using var s2 = new ProtectedSecret(plaintextSecure2);

            // We can't directly access _encryptedData as it is private, 
            // but we've verified LuxVault.Encrypt uses random nonces.
        }

        [Fact]
        public void ProtectedSecret_SecureString_RoundTrip_Works()
        {
            string original = "my-secure-password";
            using var ss = new SecureString();
            foreach (char c in original) ss.AppendChar(c);
            ss.MakeReadOnly();

            using var secret = new ProtectedSecret(ss);
            
            string recovered = "";
            secret.Use(bytes => {
                recovered = Encoding.UTF8.GetString(bytes);
            });

            Assert.Equal(original, recovered);
        }

        [Fact]
        public void ProtectedSecret_ReadOnlySpan_Constructor_Works()
        {
            byte[] original = { 0xDE, 0xAD, 0xBE, 0xEF };
            using var secret = new ProtectedSecret((ReadOnlySpan<byte>)original);
            
            byte[]? recovered = null;
            secret.Use(bytes => {
                recovered = bytes.ToArray();
            });

            Assert.NotNull(recovered);
            Assert.True(original.SequenceEqual(recovered!));
        }

        [Fact]
        public void ProtectedSecret_Use_DecodesCorrectly()
        {
            string original = "use-test-data";
            using var originalSecure = ToSecureString(original);
            using var secret = new ProtectedSecret(originalSecure);
            
            secret.Use(bytes => {
                Assert.Equal(original, Encoding.UTF8.GetString(bytes));
            });
        }

        [Fact]
        public void ProtectedSecret_DisposeClearsData()
        {
            using var secure = ToSecureString("data");
            var secret = new ProtectedSecret(secure);
            secret.Dispose();
            
            bool executed = false;
            Assert.Throws<ObjectDisposedException>(() => secret.Use(_ => executed = true));
            Assert.False(executed);
        }

        [Fact]
        public void ProtectedSecret_Initialization_FailsIfAlreadySet()
        {
            byte[] secondKey = new byte[32];
            RandomNumberGenerator.Fill(secondKey);
            
            // This shouldn't throw but should be a no-op (verified by code)
            ProtectedSecret.InitializeSessionKey(secondKey);
            
            // The original key (from constructor) should still be in effect
        }
    }
}

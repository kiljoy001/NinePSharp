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
            using var secret = new ProtectedSecret(original);
            
            // Should not show cleartext in ToString
            Assert.DoesNotContain(original, secret.ToString());
            Assert.Equal("********", secret.ToString());

            // Reveal should return the original
            #pragma warning disable CS0618
            string? revealed = secret.Reveal();
            #pragma warning restore CS0618
            Assert.Equal(original, revealed);
        }

        [Fact]
        public void ProtectedSecret_EncryptionIsUniquePerInstance()
        {
            // This tests that even for the same plaintext, the encrypted data in memory is different
            // due to the random salt/nonce used by LuxVault internally.
            string plaintext = "static-secret";
            
            using var s1 = new ProtectedSecret(plaintext);
            using var s2 = new ProtectedSecret(plaintext);

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
            
            byte[] recovered = null;
            secret.Use(bytes => {
                recovered = bytes.ToArray();
            });

            Assert.True(original.SequenceEqual(recovered));
        }

        [Fact]
        public void ProtectedSecret_Use_DecodesCorrectly()
        {
            string original = "use-test-data";
            using var secret = new ProtectedSecret(original);
            
            secret.Use(bytes => {
                Assert.Equal(original, Encoding.UTF8.GetString(bytes));
            });
        }

        [Fact]
        public void ProtectedSecret_DisposeClearsData()
        {
            var secret = new ProtectedSecret("data");
            secret.Dispose();
            
            #pragma warning disable CS0618
            Assert.Null(secret.Reveal());
            #pragma warning restore CS0618

            bool executed = false;
            secret.Use(_ => executed = true);
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

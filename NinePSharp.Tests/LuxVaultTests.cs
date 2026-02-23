using System;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class LuxVaultTests
    {
        [Fact]
        public void LuxVault_RoundTrip_Works()
        {
            var pk = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var password = "secret_password";

            // 1. Encrypt Payload
            byte[] ciphertext = LuxVault.Encrypt(Encoding.UTF8.GetBytes(pk), password);
            Assert.NotNull(ciphertext);
            
            // Overheads: Salt(16) + Nonce(24) + Mac(16) = 56 bytes
            Assert.Equal(pk.Length + 56, ciphertext.Length);

            // 2. Decrypt with correct password
            var recoveredPk = LuxVault.Decrypt(ciphertext, password);
            Assert.Equal(pk, recoveredPk);

            // 3. Decrypt with wrong password (should return null safely)
            var wrongPk = LuxVault.Decrypt(ciphertext, "wrong_password");
            Assert.Null(wrongPk);
        }

        [Fact]
        public void LuxVault_HiddenId_Derivation_Consistent()
        {
            // Seed MUST be exactly 32 bytes
            byte[] seed = new byte[32];
            RandomNumberGenerator.Fill(seed);

            string id1 = LuxVault.GenerateHiddenId(seed);
            string id2 = LuxVault.GenerateHiddenId(seed);

            // Output should be a 64-character (32-byte) hex string
            Assert.Equal(64, id1.Length);
            
            // Output should be deterministic for the same seed
            Assert.Equal(id1, id2);

            // Assert output changes based on different seed
            byte[] differentSeed = new byte[32];
            RandomNumberGenerator.Fill(differentSeed);
            string id3 = LuxVault.GenerateHiddenId(differentSeed);

            Assert.NotEqual(id1, id3);
        }

        [Fact]
        public void LuxVault_ArbitraryData_Works()
        {
            byte[] secretData = { 0xDE, 0xAD, 0xBE, 0xEF, 0x13, 0x37 };
            var password = "super_secret";

            var encrypted = LuxVault.Encrypt(secretData, password);
            using var decrypted = LuxVault.DecryptToBytes(encrypted, password);

            Assert.NotNull(decrypted);
            Assert.Equal(secretData, decrypted.Span.ToArray());
        }

        [Fact]
        public void LuxVault_ConfigProtection_Works()
        {
            var masterKey = Encoding.UTF8.GetBytes("master_key_123");
            var plainSecret = "api_key_xyz";

            var protectedSecret = LuxVault.ProtectConfig(plainSecret, masterKey);
            Assert.StartsWith("secret://", protectedSecret);

            var recovered = LuxVault.UnprotectConfig(protectedSecret, masterKey);
            Assert.Equal(plainSecret, recovered);

            // Verify it handles non-secret strings
            var normalString = "not_a_secret";
            Assert.Equal(normalString, LuxVault.UnprotectConfig(normalString, masterKey));
        }
    }
}

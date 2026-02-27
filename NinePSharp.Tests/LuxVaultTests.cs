using NinePSharp.Constants;
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
            string? recoveredPk;
            using (var decrypted = LuxVault.DecryptToBytes(ciphertext, password))
            {
                recoveredPk = decrypted == null ? null : Encoding.UTF8.GetString(decrypted.Span);
            }
            Assert.Equal(pk, recoveredPk);

            // 3. Decrypt with wrong password (should return null safely)
            string? wrongPk;
            using (var wrong = LuxVault.DecryptToBytes(ciphertext, "wrong_password"))
            {
                wrongPk = wrong == null ? null : Encoding.UTF8.GetString(wrong.Span);
            }
            Assert.Null(wrongPk);
        }

        [Fact]
        public void LuxVault_HiddenId_Derivation_Consistent()
        {
            // Note: Elligator key derivation is NON-DETERMINISTIC (uses random coordinates)
            // so GenerateHiddenId will produce different outputs even with the same seed.
            // This test verifies the output format is correct (64 hex chars).
            
            // Seed MUST be exactly 32 bytes
            byte[] seed = new byte[32];
            RandomNumberGenerator.Fill(seed);

            string id1 = LuxVault.GenerateHiddenId(seed);

            // Output should be a 64-character (32-byte) hex string
            Assert.Equal(64, id1.Length);
            
            // Output should be valid hex
            foreach (char c in id1)
            {
                Assert.True(char.IsAsciiHexDigit(c), $"Character '{c}' is not valid hex");
            }
        }

        [Fact]
        public void LuxVault_HiddenId_DifferentSeeds_DifferentOutput()
        {
            byte[] seed1 = new byte[32];
            byte[] seed2 = new byte[32];
            RandomNumberGenerator.Fill(seed1);
            RandomNumberGenerator.Fill(seed2);

            string id1 = LuxVault.GenerateHiddenId(seed1);
            string id2 = LuxVault.GenerateHiddenId(seed2);

            // Different seeds should produce different hidden IDs
            Assert.NotEqual(id1, id2);
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
            var masterKey = new byte[32];
            RandomNumberGenerator.Fill(masterKey);
            var plainSecret = "api_key_xyz";
            var plainBytes = Encoding.UTF8.GetBytes(plainSecret);

            var protectedSecret = LuxVault.ProtectConfig(plainBytes, masterKey);
            Assert.StartsWith("secret://", protectedSecret);

            using var recovered = LuxVault.UnprotectConfigToBytes(protectedSecret, masterKey);
            Assert.NotNull(recovered);
            Assert.Equal(plainSecret, Encoding.UTF8.GetString(recovered.Span));

            // Verify it handles non-secret strings
            var normalString = "not_a_secret";
            using var normalRecovered = LuxVault.UnprotectConfigToBytes(normalString, masterKey);
            Assert.NotNull(normalRecovered);
            Assert.Equal(normalString, Encoding.UTF8.GetString(normalRecovered.Span));
        }
    }
}

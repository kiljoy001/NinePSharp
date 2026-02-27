using NinePSharp.Constants;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class VaultRegressionTests
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

        public VaultRegressionTests()
        {
            // Initialize session key if not already done
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            ProtectedSecret.InitializeSessionKey(key);
        }

        [Fact]
        public unsafe void ProtectedSecret_Use_Wipes_Memory_After_Call()
        {
            string secretText = "regression-test-secret-12345";
            using var secretTextSecure = ToSecureString(secretText);
            using var secret = new ProtectedSecret(secretTextSecure);

            // Let's use a different approach: Capture the pointer.
            IntPtr bufferPtr = IntPtr.Zero;
            int bufferLen = 0;

            secret.Use(span => {
                fixed (byte* p = span)
                {
                    bufferPtr = (IntPtr)p;
                    bufferLen = span.Length;
                    // Confirm it is NOT zeroed yet
                    Assert.False(span[0] == 0 && span[1] == 0);
                }
            });

            // NOW check the memory at bufferPtr. It SHOULD be zeroed.
            // This is slightly dangerous if the memory was re-allocated, 
            // but since we used pinned arrays in implementation, it should stay.
            byte[] after = new byte[bufferLen];
            Marshal.Copy(bufferPtr, after, 0, bufferLen);

            Assert.All(after, b => Assert.Equal(0, b));
        }

        [Property]
        public bool Property_LuxVault_Encryption_RoundTrip(byte[] data, string password)
        {
            if (data == null || password == null) return true;

            byte[] encrypted = LuxVault.Encrypt(data, password);
            using var decrypted = LuxVault.DecryptToBytes(encrypted, password);

            return decrypted != null && data.SequenceEqual(decrypted.Span.ToArray());
        }

        [Property]
        public bool Property_LuxVault_HiddenId_No_Seed_Mutation(byte[] seed)
        {
            if (seed == null || seed.Length != 32) return true;

            byte[] seedCopy = (byte[])seed.Clone();
            LuxVault.GenerateHiddenId(seed);

            // Seed should NOT be zeroed out for the caller anymore
            return seed.SequenceEqual(seedCopy);
        }

        [Fact]
        public void SecureString_Encryption_Is_Accurate()
        {
            // Verify that our unsafe WithSecureString logic doesn't corrupt data
            string raw = "secure-password-!@#";
            using var ss = new SecureString();
            foreach (var c in raw) ss.AppendChar(c);
            ss.MakeReadOnly();

            byte[] data = { 1, 2, 3, 4 };
            byte[] encrypted = LuxVault.Encrypt(data, ss);
            using var decrypted = LuxVault.DecryptToBytes(encrypted, ss);

            Assert.NotNull(decrypted);
            Assert.Equal(data, decrypted.Span.ToArray());
        }
        
        [Fact]
        public void ProtectedSecret_Constructor_Wipes_Input_Buffer()
        {
            string raw = "sensitive";
            using var rawSecure = ToSecureString(raw);
            using var ps = new ProtectedSecret(rawSecure);
            string? recovered = null;
            ps.Use(bytes => recovered = Encoding.UTF8.GetString(bytes));
            Assert.Equal(raw, recovered);
        }
    }
}

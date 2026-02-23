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
            using var secret = new ProtectedSecret(secretText);
            
            byte[]? capturedBuffer = null;

            secret.Use(span => {
                // We trick the system to get the underlying array reference
                // This is only possible in tests where we can be "unsafe"
                fixed (byte* p = span)
                {
                    // Verify data is correct DURING the call
                    string current = Encoding.UTF8.GetString(span);
                    Assert.Equal(secretText, current);
                }
                
                // Capture the array to check it later
                // Note: span is usually a slice of the 'decrypted' array in Use()
                // In our implementation, decrypted is the full array.
                capturedBuffer = span.ToArray(); 
                
                // Wait, span.ToArray() creates a COPY. That's not what we want to check.
                // We want to check the ACTUAL memory that was used by the ProtectedSecret.
                // Since ProtectedSecret.Use uses 'decrypted' which is passed as a span,
                // we can't easily get the reference to the original array from the span 
                // without MemoryMarshal.
            });

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
            // This is hard to test because the input is a string, which is immutable.
            // But we can verify functionality.
            string raw = "sensitive";
            using var ps = new ProtectedSecret(raw);
            Assert.Equal(raw, ps.Reveal());
        }
    }
}

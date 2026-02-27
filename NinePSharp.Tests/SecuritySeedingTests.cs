using NinePSharp.Constants;
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class SecuritySeedingTests
    {
        [Fact]
        public void SeedDerivation_ProducesCorrectKeyLength()
        {
            // Simulate the 64-bit seed logic from Program.cs
            byte[] seedBytes = new byte[8]; 
            RandomNumberGenerator.Fill(seedBytes);
            var hex = Convert.ToHexString(seedBytes);
            
            using var secure = new SecureString();
            foreach (var c in hex) secure.AppendChar(c);
            secure.MakeReadOnly();

            // Extract and hash
            byte[] hashedKey = new byte[32];
            IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            try
            {
                string recoveredHex = Marshal.PtrToStringUni(ptr)!;
                byte[] recoveredSeed = Convert.FromHexString(recoveredHex);
                
                Assert.Equal(seedBytes, recoveredSeed);

                MonocypherNative.crypto_blake2b(hashedKey, (nuint)hashedKey.Length, recoveredSeed, (nuint)recoveredSeed.Length);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }

            Assert.Equal(32, hashedKey.Length);
            Assert.False(hashedKey.All(b => b == 0));
        }

        [Fact]
        public void Monocypher_Blake2b_IsDeterministic()
        {
            byte[] data = Encoding.UTF8.GetBytes("test-data-for-blake2b");
            byte[] h1 = new byte[32];
            byte[] h2 = new byte[32];

            MonocypherNative.crypto_blake2b(h1, (nuint)h1.Length, data, (nuint)data.Length);
            MonocypherNative.crypto_blake2b(h2, (nuint)h2.Length, data, (nuint)data.Length);

            Assert.Equal(h1, h2);
        }
    }
}

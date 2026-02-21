using System;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class MonocypherSanityTests
    {
        [Fact]
        public void Monocypher_Elligator_IsDeterministic()
        {
            byte[] seed = new byte[32];
            RandomNumberGenerator.Fill(seed);
            
            byte[] seedCopy = (byte[])seed.Clone();

            byte[] h1 = new byte[32];
            byte[] s1 = new byte[32];
            MonocypherNative.crypto_elligator_key_pair(h1, s1, seed);

            byte[] h2 = new byte[32];
            byte[] s2 = new byte[32];
            MonocypherNative.crypto_elligator_key_pair(h2, s2, seed);

            Assert.Equal(seedCopy, seed); // Ensure seed was NOT mutated
            Assert.Equal(h1, h2);
        }
    }
}

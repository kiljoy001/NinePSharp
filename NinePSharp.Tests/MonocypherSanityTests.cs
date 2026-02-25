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

            // Monocypher's crypto_elligator_key_pair mutates (wipes) the provided seed.
            // Use two independent copies so we can still test determinism.
            byte[] seedForFirst = (byte[])seed.Clone();
            byte[] seedForSecond = (byte[])seed.Clone();

            byte[] h1 = new byte[32];
            byte[] s1 = new byte[32];
            byte[] h2 = new byte[32];
            byte[] s2 = new byte[32];

            unsafe {
                fixed (byte* ph1 = h1, ps1 = s1, pseed1 = seedForFirst)
                fixed (byte* ph2 = h2, ps2 = s2, pseed2 = seedForSecond) {
                    MonocypherNative.crypto_elligator_key_pair(ph1, ps1, pseed1);
                    MonocypherNative.crypto_elligator_key_pair(ph2, ps2, pseed2);
                }
            }

            Assert.Equal(h1, h2);
            Assert.Equal(s1, s2);

            // Native function should wipe seed buffers after use.
            Assert.All(seedForFirst, b => Assert.Equal(0, b));
            Assert.All(seedForSecond, b => Assert.Equal(0, b));
        }
    }
}

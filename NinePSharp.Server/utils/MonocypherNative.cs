using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils
{
    public static class MonocypherNative
    {
        private const string LibName = "monocypher";

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_elligator_key_pair(byte[] hidden, byte[] secret_key, byte[] seed);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_elligator_map(byte[] public_key, byte[] hidden);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_elligator_rev(byte[] hidden, byte[] public_key, byte tweak);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_x25519(byte[] shared_secret, byte[] secret_key, byte[] public_key);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_hchacha20(byte[] out_key, byte[] in_key, byte[] in_nonce);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_blake2b(byte[] hash, nuint hash_size, byte[] message, nuint message_size);

        // XChaCha20-Poly1305 (24-byte nonce)
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_aead_lock(byte[] cipher_text, byte[] mac, byte[] key, byte[] nonce, byte[]? ad, nuint ad_size, byte[] plain_text, nuint text_size);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_aead_unlock(byte[] plain_text, byte[] mac, byte[] key, byte[] nonce, byte[]? ad, nuint ad_size, byte[] cipher_text, nuint text_size);
    }
}

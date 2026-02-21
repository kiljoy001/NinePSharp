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
        public static extern void crypto_blake2b(byte[] hash, byte[] data, ulong data_size);
    }
}

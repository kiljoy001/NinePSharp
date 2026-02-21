using System;
using System.Runtime.InteropServices;

namespace NinePSharp.Server.Utils
{
    public static class MonocypherNative
    {
        private const string LibName = "monocypher";

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void crypto_elligator_key_pair(byte[] hidden, byte[] secret_key, byte[] seed);
    }
}

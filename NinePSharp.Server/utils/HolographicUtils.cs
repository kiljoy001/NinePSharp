using System;

namespace NinePSharp.Server.Utils
{
    public static class HolographicUtils
    {
        public const ulong WAVE_MASK = 0x7;
        public const ulong WAVE_6_INVISIBLE_LOCK = 0x6; // 110
        public const ulong WAVE_7_DISTRESS = 0x7;       // 111

        /// <summary>
        /// Encodes a wave signal into a 64-bit path.
        /// Assumes the path is 8-byte aligned (lower 3 bits are 0).
        /// </summary>
        public static ulong EncodeWave(ulong path, ulong wave)
        {
            return (path & ~WAVE_MASK) | (wave & WAVE_MASK);
        }

        /// <summary>
        /// Decodes the wave signal from a 64-bit path.
        /// </summary>
        public static ulong DecodeWave(ulong path)
        {
            return path & WAVE_MASK;
        }

        /// <summary>
        /// Clears the wave signal from a path, returning the "clean" path.
        /// </summary>
        public static ulong GetCleanPath(ulong path)
        {
            return path & ~WAVE_MASK;
        }

        /// <summary>
        /// Checks if a path has a specific wave signal.
        /// </summary>
        public static bool IsWave(ulong path, ulong wave)
        {
            return DecodeWave(path) == wave;
        }
    }
}

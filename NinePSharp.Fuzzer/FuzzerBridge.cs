using System;
using System.Linq;
using NinePSharp.Constants;
using NinePSharp.Parser;

namespace NinePSharp.Fuzzer
{
    public static class FuzzerBridge
    {
        private static readonly System.Random _rnd = new System.Random();
        private static readonly string[][] TraversalPayloads =
        [
            ["..", "..", "etc", "passwd"],
            ["..", "..", "windows", "system32"],
            ["/"],
            ["", "..", ""],
            ["..", ".", "..", "."],
            ["$", "{IFS}", "..", "etc", "passwd"],
        ];

        public static byte[] GenerateValid(MessageTypes type)
        {
            // Load a random sample from pre-generated corpus
            var corpusDir = "corpus/parser/valid";
            if (System.IO.Directory.Exists(corpusDir))
            {
                var files = System.IO.Directory.GetFiles(corpusDir, "*.bin");
                if (files.Length > 0)
                {
                    var randomFile = files[_rnd.Next(files.Length)];
                    return System.IO.File.ReadAllBytes(randomFile);
                }
            }
            // Fallback to simple valid Tversion message
            return new byte[] { 19, 0, 0, 0, 100, 0, 0, 255, 255, 0, 0, 8, 0, 0, 0, 57, 80, 50, 48, 48, 48, 46, 76 };
        }

        public static byte[] Mutate(byte[] input)
        {
            var result = NinePParser.parse(true, input);
            if (result.IsOk)
            {
                // For parsed messages, use byte-level mutations which are more reliable
                // than trying to do semantic mutations across F#-C# boundary
                return MutateBytes(input);
            }

            // Fallback to simple bit-flip mutation
            byte[] mutated = (byte[])input.Clone();
            if (mutated.Length > 7)
            {
                int idx = _rnd.Next(7, mutated.Length);
                mutated[idx] = (byte)(mutated[idx] ^ 0xFF);
            }
            return mutated;
        }

        public static byte[] MutateBytes(byte[] input)
        {
            if (input.Length == 0)
            {
                return BitFlip(input, 1);
            }

            return _rnd.Next(4) switch
            {
                0 => BitFlip(input, Math.Max(1, input.Length / 32)),
                1 => ByteReplace(input, Math.Max(1, input.Length / 32)),
                2 => ByteInsert(input, Math.Max(1, input.Length / 64)),
                _ => ByteDelete(input, Math.Max(1, input.Length / 64))
            };
        }

        public static byte[] BitFlip(byte[] input, int count)
        {
            var output = (byte[])input.Clone();
            if (output.Length == 0)
            {
                return [0xFF];
            }

            for (int i = 0; i < count; i++)
            {
                int index = _rnd.Next(output.Length);
                int bit = _rnd.Next(8);
                output[index] ^= (byte)(1 << bit);
            }

            return output;
        }

        public static byte[] ByteReplace(byte[] input, int count)
        {
            var output = (byte[])input.Clone();
            if (output.Length == 0)
            {
                return [0x00];
            }

            for (int i = 0; i < count; i++)
            {
                int index = _rnd.Next(output.Length);
                output[index] = (byte)_rnd.Next(256);
            }

            return output;
        }

        public static byte[] ByteInsert(byte[] input, int count)
        {
            var inserted = Enumerable.Range(0, Math.Max(1, count))
                .Select(_ => (byte)_rnd.Next(256))
                .ToArray();

            if (input.Length == 0)
            {
                return inserted;
            }

            int index = _rnd.Next(input.Length + 1);
            var output = new byte[input.Length + inserted.Length];
            Buffer.BlockCopy(input, 0, output, 0, index);
            Buffer.BlockCopy(inserted, 0, output, index, inserted.Length);
            Buffer.BlockCopy(input, index, output, index + inserted.Length, input.Length - index);
            return output;
        }

        public static byte[] ByteDelete(byte[] input, int count)
        {
            if (input.Length == 0)
            {
                return [];
            }

            int deleteCount = Math.Min(Math.Max(1, count), input.Length);
            int index = _rnd.Next(input.Length - deleteCount + 1);
            var output = new byte[input.Length - deleteCount];
            Buffer.BlockCopy(input, 0, output, 0, index);
            Buffer.BlockCopy(input, index + deleteCount, output, index, input.Length - index - deleteCount);
            return output;
        }

        public static string[] GenerateValidPath()
        {
            return TraversalPayloads[_rnd.Next(TraversalPayloads.Length)];
        }

        public static string[] MutatePath(string[] path)
        {
            // Use pre-generated malicious paths for better coverage
            _ = path;
            return TraversalPayloads[_rnd.Next(TraversalPayloads.Length)];
        }

        public static byte[][] GenerateAttackVectors()
        {
            return
            [
                [],
                [0x00],
                [0xFF],
                [0x00, 0x00, 0x00, 0x00],
                [0xFF, 0xFF, 0xFF, 0xFF],
                BitFlip(GenerateValid(MessageTypes.Tversion), 4),
                ByteInsert(GenerateValid(MessageTypes.Tauth), 8),
                ByteDelete(GenerateValid(MessageTypes.Twalk), 4)
            ];
        }

        public static string[][] GeneratePathTraversals()
        {
            return TraversalPayloads;
        }

    }
}

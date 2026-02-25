using System;
using System.Linq;
using FsCheck;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Generators;

namespace NinePSharp.Fuzzer
{
    public static class FuzzerBridge
    {
        private static readonly System.Random _rnd = new System.Random();

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
                return Mutation.mutateBytes(input);
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
            return Mutation.mutateBytes(input);
        }

        public static byte[] BitFlip(byte[] input, int count)
        {
            return Mutation.bitFlip(input, count);
        }

        public static byte[] ByteReplace(byte[] input, int count)
        {
            return Mutation.byteReplace(input, count);
        }

        public static byte[] ByteInsert(byte[] input, int count)
        {
            return Mutation.byteInsert(input, count);
        }

        public static byte[] ByteDelete(byte[] input, int count)
        {
            return Mutation.byteDelete(input, count);
        }

        public static string[] GenerateValidPath()
        {
            var paths = Mutation.generatePathTraversals();
            return paths[_rnd.Next(paths.Length)];
        }

        public static string[] MutatePath(string[] path)
        {
            // Use pre-generated malicious paths for better coverage
            var paths = Mutation.generatePathTraversals();
            return paths[_rnd.Next(paths.Length)];
        }

        public static byte[][] GenerateAttackVectors()
        {
            return Mutation.generateAttackVectors();
        }

        public static string[][] GeneratePathTraversals()
        {
            return Mutation.generatePathTraversals();
        }

    }
}

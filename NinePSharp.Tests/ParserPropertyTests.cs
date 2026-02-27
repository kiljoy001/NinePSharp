using NinePSharp.Constants;
using System;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Parser;
using Xunit;

namespace NinePSharp.Tests;

public class ParserPropertyTests
{
    [Property]
    public bool Parser_Never_Crashes_On_Random_Bytes(byte[] data)
    {
        if (data == null) return true;

        try
        {
            // Fuzzing logic: feed random bytes to the parser
            // We expect Ok or Error, but NO Exceptions.
            var result = NinePParser.parse(NinePDialect.NineP2000, data.AsMemory());
            
            // If it returns Ok, the data happened to be a valid message.
            // If it returns Error, it handled the malformed data correctly.
            // The property holds true in both cases.
            return true;
        }
        catch (Exception ex)
        {
            // If we catch an exception, the parser failed to handle the input gracefully.
            Console.WriteLine($"Parser crashed with: {ex}");
            return false;
        }
    }

    [Property]
    public bool Parser_9u_Never_Crashes_On_Random_Bytes(byte[] data)
    {
        if (data == null) return true;

        try
        {
            var result = NinePParser.parse(NinePDialect.NineP2000U, data.AsMemory());
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parser (9u) crashed with: {ex}");
            return false;
        }
    }
}

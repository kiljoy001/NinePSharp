using NinePSharp.Constants;
using System;
using System.Linq;
using NinePSharp.Parser;
using NinePSharp.Generators;
using Xunit;

namespace NinePSharp.Tests;

public class ParserRobustnessTests
{
    [Fact]
    public void Parser_Handles_Partial_Messages_Gracefully()
    {
        var twalk = new NinePSharp.Messages.Twalk(1, 1, 2, new[] { "home", "user" });
        var fullBytes = new byte[twalk.Size];
        twalk.WriteTo(fullBytes);

        for (int i = 1; i < fullBytes.Length; i++)
        {
            var partial = fullBytes.AsMemory(0, i);
            var result = NinePParser.parse(NinePDialect.NineP2000U, partial);
            
            // Should return Error (too short), never crash
            Assert.True(result.IsError, $"Parser should return Error for length {i}, but was {result}");
        }
    }

    [Fact]
    public void Parser_Handles_Zero_Length_Buffer()
    {
        var result = NinePParser.parse(NinePDialect.NineP2000U, Memory<byte>.Empty);
        Assert.True(result.IsError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)] // Just before type byte
    public void Parser_Handles_Too_Small_Header(int length)
    {
        var buffer = new byte[length];
        var result = NinePParser.parse(NinePDialect.NineP2000U, buffer.AsMemory());
        Assert.True(result.IsError);
    }

    [Fact]
    public void Parser_Rejects_Message_Size_Smaller_Than_Header()
    {
        var buffer = new byte[7];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer, 6); // Invalid size < 7
        buffer[4] = (byte)Constants.MessageTypes.Tversion;
        
        var result = NinePParser.parse(NinePDialect.NineP2000U, buffer.AsMemory());
        Assert.True(result.IsError);
    }

    [Fact]
    public void Parser_Fuzzing_Large_Buffer_With_Random_Garbage()
    {
        var rnd = new Random(42);
        var buffer = new byte[1024 * 1024]; // 1MB
        rnd.NextBytes(buffer);

        // Try parsing at various offsets
        for (int i = 0; i < 1000; i++)
        {
            int offset = rnd.Next(0, buffer.Length - 100);
            int len = rnd.Next(1, 100);
            
            // Should not crash
            NinePParser.parse(NinePDialect.NineP2000U, buffer.AsMemory(offset, len));
        }
    }
}

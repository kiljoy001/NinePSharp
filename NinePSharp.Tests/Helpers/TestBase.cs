using NinePSharp.Constants;
using System;
using System.Text;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using Xunit;

namespace NinePSharp.Tests.Helpers;

public abstract class TestBase
{
    protected void RoundTripTest<T>(uint size, ushort tag, Func<ReadOnlySpan<byte>, T> deserializer, Action<T> assertFunc, Func<T> instantiator, Action<T, Span<byte>> serializer)
    {
        var original = instantiator();
        var buffer = new byte[size];
        serializer(original, buffer);
        
        var msgSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan()[..4]);
        Assert.Equal(size, msgSize);
        
        var msgTag = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan().Slice(5, 2));
        Assert.Equal(tag, msgTag);

        var deserialized = deserializer(buffer);
        assertFunc(deserialized);
    }
}

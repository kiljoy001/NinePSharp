using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;

namespace NinePSharp.Backends.Pipes.Tests;

internal static class PipeTestHelpers
{
    internal static Tcreate BuildCreate(ushort tag, uint fid, string name, uint perm = 0755, byte mode = NinePConstants.ORDWR)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameBytes.Length + 4 + 1);
        var buffer = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), size);
        buffer[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), fid);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(buffer.AsSpan(offset, nameBytes.Length));
        offset += nameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), perm);
        offset += 4;
        buffer[offset] = mode;

        return new Tcreate(buffer);
    }
}

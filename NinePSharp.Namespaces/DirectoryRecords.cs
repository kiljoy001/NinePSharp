using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

/// <summary>Validates raw directory records without the ushort total-size limit of Stat.</summary>
internal static class DirectoryRecords
{
    internal static void Validate(ReadOnlySpan<byte> bytes, uint count)
    {
        if ((uint)bytes.Length > count)
        {
            throw new IOException("provider returned more directory bytes than requested");
        }

        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 49)
            {
                throw new IOException("truncated directory record");
            }

            int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes) + 2;
            if (size < 49 || size > bytes.Length)
            {
                throw new IOException("invalid directory record size");
            }

            ReadOnlySpan<byte> record = bytes[..size];
            int position = 41;
            for (int field = 0; field < 4; field++)
            {
                if (position + 2 > size)
                {
                    throw new IOException("truncated directory string prefix");
                }

                int length = BinaryPrimitives.ReadUInt16LittleEndian(record[position..]);
                position += 2 + length;
            }

            if (position != size)
            {
                throw new IOException("trailing bytes in directory record");
            }

            bytes = bytes[size..];
        }
    }
}

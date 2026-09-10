using System.Buffers.Binary;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;

namespace NinePSharp.Namespaces.Orleans.Server;

internal static class LinuxProtocol
{
    private const uint AccessMask = 0x3;
    private const uint Create = 0x40;
    private const uint Exclusive = 0x80;
    private const uint Truncate = 0x200;
    private const uint LargeFile = 0x8000;
    private const uint Directory = 0x10000;
    private const uint NoFollow = 0x20000;
    private const uint CloseOnExec = 0x80000;
    private const uint SupportedOpenFlags = AccessMask | Truncate | LargeFile | Directory | NoFollow | CloseOnExec;
    private const uint SupportedCreateFlags = SupportedOpenFlags | Create | Exclusive;

    private const uint DirectoryMode = 0x4000;
    private const uint RegularMode = 0x8000;
    private const uint SymbolicLinkMode = 0xA000;
    private const byte DirectoryEntryUnknown = 0;
    private const byte DirectoryEntryDirectory = 4;
    private const byte DirectoryEntryRegular = 8;
    private const byte DirectoryEntrySymbolicLink = 10;

    private const ulong SupportedAttributes =
        (ulong)NinePConstants.GetAttrMask.P9_GETATTR_MODE
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_NLINK
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_ATIME
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_MTIME
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_CTIME
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_INO
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_SIZE
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BLOCKS
        | (ulong)NinePConstants.GetAttrMask.P9_GETATTR_DATA_VERSION;

    internal static byte ToOpenMode(uint flags, bool creating = false)
    {
        uint supported = creating ? SupportedCreateFlags : SupportedOpenFlags;
        if ((flags & ~supported) != 0 || (flags & AccessMask) == AccessMask)
        {
            throw new NotSupportedException("unsupported 9P2000.L open flags");
        }

        byte mode = checked((byte)(flags & AccessMask));
        return (flags & Truncate) == 0 ? mode : (byte)(mode | NinePConstants.OTRUNC);
    }

    internal static bool RequiresDirectory(uint flags) => (flags & Directory) != 0;

    internal static ReadOnlyMemory<byte> EncodeDirectory(
        IReadOnlyList<ResourceStat> entries,
        ulong requestedOffset = 0,
        uint count = uint.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ulong cookie = 0;
        int startIndex = 0;
        while (startIndex < entries.Count && cookie < requestedOffset)
        {
            cookie = checked(cookie + (ulong)EntryLength(entries[startIndex]));
            startIndex++;
        }

        if (cookie < requestedOffset)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (cookie != requestedOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedOffset), "offset is not a directory cookie");
        }

        int budget = checked((int)Math.Min(count, int.MaxValue));
        int length = 0;
        int endIndex = startIndex;
        while (endIndex < entries.Count)
        {
            int entryLength = EntryLength(entries[endIndex]);
            if (entryLength > budget - length)
            {
                break;
            }

            length += entryLength;
            endIndex++;
        }

        byte[] data = new byte[length];
        int offset = 0;
        for (int index = startIndex; index < endIndex; index++)
        {
            ResourceStat entry = entries[index];
            int nextOffset = checked(offset + EntryLength(entry));
            cookie = checked(cookie + (ulong)EntryLength(entry));
            data.AsSpan().WriteQid(entry.Resource.Qid, ref offset);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), cookie);
            offset += 8;
            data[offset++] = DirectoryEntryType(entry.Resource.Type);
            data.AsSpan().WriteString(entry.Name, ref offset);
            System.Diagnostics.Debug.Assert(offset == nextOffset);
        }

        return data;
    }

    internal static Rgetattr ToGetAttr(Tgetattr request, ResourceStat stat)
    {
        ulong valid = request.RequestMask & SupportedAttributes;
        ulong blocks = stat.Length / 512 + (stat.Length % 512 == 0 ? 0UL : 1UL);
        return new Rgetattr(
            request.Tag,
            valid,
            stat.Resource.Qid,
            ToPosixMode(stat),
            0,
            0,
            1,
            0,
            stat.Length,
            4096,
            blocks,
            stat.AccessTime,
            0,
            stat.ModificationTime,
            0,
            stat.ModificationTime,
            0,
            0,
            0,
            0,
            stat.Resource.Qid.Version);
    }

    private static int EntryLength(ResourceStat entry)
        => checked(13 + 8 + 1 + 2 + Encoding.UTF8.GetByteCount(entry.Name));

    private static byte DirectoryEntryType(QidType type)
    {
        if ((type & QidType.QTDIR) != 0)
        {
            return DirectoryEntryDirectory;
        }

        if ((type & QidType.QTSYMLINK) != 0)
        {
            return DirectoryEntrySymbolicLink;
        }

        return type == QidType.QTFILE ? DirectoryEntryRegular : DirectoryEntryUnknown;
    }

    private static uint ToPosixMode(ResourceStat stat)
    {
        uint fileType = stat.Resource.IsDirectory
            ? DirectoryMode
            : (stat.Resource.Type & QidType.QTSYMLINK) != 0
                ? SymbolicLinkMode
                : RegularMode;
        return fileType | (stat.Mode & 0xFFF);
    }
}

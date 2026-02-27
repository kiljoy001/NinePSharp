using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;

namespace NinePSharp.Backends.PowerShell.Tests;

internal static class PowerShellTestHelpers
{
    public static Tcreate BuildCreate(ushort tag, uint fid, string name, uint perm = 0755, byte mode = NinePConstants.ORDWR)
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

    public static async Task<IReadOnlyList<string>> ListJobsAsync(PowerShellFileSystem root, ushort tagBase = 200)
    {
        var jobs = (PowerShellFileSystem)root.Clone();
        await jobs.WalkAsync(new Twalk(tagBase, 1, 1, new[] { "jobs" }));
        var read = await jobs.ReadAsync(new Tread((ushort)(tagBase + 1), 1, 0, ushort.MaxValue));
        return ParseStatNames(read.Data.Span);
    }

    public static IReadOnlyList<string> ParseStatNames(ReadOnlySpan<byte> bytes)
    {
        var names = new List<string>();
        int offset = 0;

        while (offset + 2 <= bytes.Length)
        {
            int entryStart = offset;
            try
            {
                var stat = new Stat(bytes, ref offset);
                if (offset <= entryStart)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(stat.Name))
                {
                    names.Add(stat.Name);
                }
            }
            catch
            {
                // If a caller reads mid-entry due paging, stop at last complete record.
                break;
            }
        }

        return names;
    }

    public static async Task<bool> RemoveJobIfPresentAsync(PowerShellFileSystem root, string jobName, ushort tagBase = 250)
    {
        var jobs = (PowerShellFileSystem)root.Clone();
        await jobs.WalkAsync(new Twalk(tagBase, 1, 1, new[] { "jobs" }));

        var listing = await jobs.ReadAsync(new Tread((ushort)(tagBase + 1), 1, 0, ushort.MaxValue));
        if (!ParseStatNames(listing.Data.Span).Contains(jobName, StringComparer.Ordinal))
        {
            return false;
        }

        var job = (PowerShellFileSystem)jobs.Clone();
        var walk = await job.WalkAsync(new Twalk((ushort)(tagBase + 2), 1, 1, new[] { jobName }));
        if (walk.Wqid.Length != 1)
        {
            return false;
        }

        await job.RemoveAsync(new Tremove((ushort)(tagBase + 3), 1));
        return true;
    }

    public static string SanitizeJobName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "job";
        }

        var sb = new StringBuilder(raw.Length);
        foreach (char ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        if (sb.Length == 0)
        {
            sb.Append("job");
        }

        if (sb.Length > 40)
        {
            return sb.ToString(0, 40);
        }

        return sb.ToString();
    }
}

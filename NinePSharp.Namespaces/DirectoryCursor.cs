using System.Text;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>
/// A shared metadata-backed directory cursor. The current provider contract supplies
/// a whole listing; this adapter returns bounded, complete native 9P2000 stat records.
/// Rewind discards the listing so the next read refreshes it.
/// </summary>
internal sealed class DirectoryCursor(Func<CancellationToken, ValueTask<IReadOnlyList<ResourceStat>>> load) : IDirectoryCursor
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<ResourceStat>? entries;
    private int index;
    private long offset;

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(long requestedOffset, uint count, CancellationToken cancellationToken,
        MountTable callingNamespace)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (requestedOffset == 0 || (requestedOffset == -1 && offset == 0)) Reset();
            if (requestedOffset != -1 && requestedOffset != offset)
                throw new NamespaceFidException("invalid directory offset");
            if (count == 0) return ReadOnlyMemory<byte>.Empty;
            entries ??= (await load(cancellationToken)).ToArray();
            int budget = (int)Math.Min(count, int.MaxValue);
            var records = new List<Stat>();
            int bytes = 0;
            int next = index;
            while (next < entries.Count)
            {
                Stat record = Encode(entries[next]);
                if (record.Size > budget - bytes) break;
                bytes += record.Size; // Bounded by budget, which is at most int.MaxValue.
                records.Add(record);
                next++;
            }
            if (bytes == 0 && next < entries.Count)
                throw new NamespaceFidException("directory read buffer is too small for the next stat record");
            var buffer = new byte[bytes];
            int position = 0;
            foreach (Stat record in records) record.WriteTo(buffer, ref position);
            // At most int.MaxValue records of ushort.MaxValue bytes per snapshot.
            offset += bytes;
            index = next;
            return buffer;
        }
        finally { gate.Release(); }
    }

    public async ValueTask RewindAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { Reset(); }
        finally { gate.Release(); }
    }

    private void Reset()
    {
        entries = null;
        index = 0;
        offset = 0;
    }

    private static Stat Encode(ResourceStat entry)
    {
        // Stat.Size uses ushort storage; validate before its unchecked size conversion.
        long size = 49L + Encoding.UTF8.GetByteCount(entry.Name) + Encoding.UTF8.GetByteCount(entry.User)
            + Encoding.UTF8.GetByteCount(entry.Group) + Encoding.UTF8.GetByteCount(entry.LastModifier);
        if (size > ushort.MaxValue) throw new IOException("directory stat record exceeds supported size");
        return new Stat((ushort)size, 0, 0, entry.Resource.Qid, entry.Mode, entry.AccessTime,
            entry.ModificationTime, entry.Length, entry.Name, entry.User, entry.Group, entry.LastModifier);
    }
}

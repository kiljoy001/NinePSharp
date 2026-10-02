using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

internal sealed class ProviderDirectoryCursor(
    INamespaceDataPlane dataPlane,
    ResourceOpenHandle outer,
    DirectoryMountHead? union,
    Func<ResourceOperationContext> contextFactory,
    IDirectoryStatOperations? stats) : IDirectoryCursor
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<byte[]> rock = new();
    private long offset;
    private ulong deviceOffset;
    private Exception? uncertainRead;
    private int memberIndex;
    private ResourceOpenHandle? member;
    private ResourceOperationContext? memberClose;
    private ulong memberOffset;

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        long requestedOffset,
        uint count,
        CancellationToken cancellationToken,
        MountTable callingNamespace)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (uncertainRead is not null)
            {
                throw new IOException("directory stream requires outcome reconciliation before further reads", uncertainRead);
            }

            long position = requestedOffset == -1 ? offset : requestedOffset;
            if (position < 0)
            {
                throw new NamespaceFidException("negative offset");
            }

            if (position == 0)
            {
                offset = 0;
                deviceOffset = 0;
                rock.Clear();
                memberIndex = 0;
                await CloseMemberAsync();
            }

            ReadOnlyMemory<byte> result;
            try
            {
                result = ReadRock(count);
                if (result.IsEmpty)
                {
                    if (union is not null)
                    {
                        result = await ReadUnionAsync(count);
                    }
                    else
                    {
                        if (position != offset)
                        {
                            throw new NamespaceFidException("invalid directory offset");
                        }

                        // Cancellation may abandon a waiter, not an admitted operation's outcome.
                        result = await dataPlane.ReadAsync(outer, deviceOffset, count, CancellationToken.None);
                    }
                }

                DirectoryRecords.Validate(result.Span, count);
                int rawLength = result.Length;
                result = await FixMountsAsync(result, count, callingNamespace);
                offset = checked(offset + result.Length);
                deviceOffset = checked(deviceOffset + (uint)rawLength);
            }
            catch (ResourceDirectoryRejectedException)
            {
                throw;
            }
            catch (NamespaceFidException)
            {
                throw;
            }
            catch (Exception error)
            {
                // A lost reply or malformed batch may have advanced provider state.
                uncertainRead = error;
                throw;
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask RewindAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            offset = 0;
            memberIndex = 0;
        } // sseek defers device/stream reset until a read at zero.
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask CloseAsync()
    {
        rock.Clear();
        await CloseMemberAsync();
    }

    private static byte[] Join(IEnumerable<byte[]> records, int length)
    {
        var result = new byte[length];
        int position = 0;
        foreach (byte[] record in records)
        {
            record.CopyTo(result, position);
            position += record.Length;
        }

        return result;
    }

    private async ValueTask CloseMemberAsync()
    {
        ResourceOpenHandle? closing = member;
        member = null;
        memberOffset = 0;
        if (closing is null)
        {
            return;
        }

        try
        {
            await dataPlane.ClunkAsync(closing, memberClose!, CancellationToken.None);
        }
        catch (Exception)
        {
        } // cclose releases local ownership even after a close error.

        memberClose = null;
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadUnionAsync(uint count)
    {
        await union!.Gate.WaitAsync();
        try
        {
            while (memberIndex < union.Members.Count)
            {
                try
                {
                    if (member is null)
                    {
                        ResourceOperationContext opening = contextFactory();
                        memberClose = contextFactory();
                        var channel = NamespaceChannel.Restore(new[] { new ChannelFrame("/", union.Members[memberIndex].Target) });
                        try
                        {
                            member = await dataPlane.OpenAsync(channel, 0, opening, CancellationToken.None);
                        }
                        catch (ResourceDirectoryRejectedException)
                        {
                            throw;
                        }
                        catch (Exception error)
                        {
                            throw new DirectoryOperationUncertainException(opening, error);
                        }
                    }

                    ReadOnlyMemory<byte> bytes = await dataPlane.ReadAsync(member, memberOffset, count, CancellationToken.None);
                    memberOffset = checked(memberOffset + (uint)bytes.Length);
                    if (!bytes.IsEmpty)
                    {
                        return bytes;
                    }
                }
                catch (ResourceDirectoryRejectedException)
                {
                }

                memberIndex++;
                await CloseMemberAsync();
            }

            return ReadOnlyMemory<byte>.Empty;
        }
        finally
        {
            union.Gate.Release();
        }
    }

    private ReadOnlyMemory<byte> ReadRock(uint count)
    {
        int length = 0;
        int records = 0;
        while (records < rock.Count && (long)length + rock[records].Length <= count)
        {
            length += rock[records++].Length;
        }

        byte[] bytes = Join(rock.Take(records), length);
        rock.RemoveRange(0, records);
        return bytes;
    }

    private async ValueTask<ReadOnlyMemory<byte>> FixMountsAsync(ReadOnlyMemory<byte> bytes, uint count, MountTable mounts)
    {
        var records = new List<byte[]>();
        for (int position = 0; position < bytes.Length;)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[position..]) + 2;
            records.Add(bytes.Slice(position, size).ToArray());
            position += size;
        }

        int length = bytes.Length;
        for (int index = 0; index < records.Count; index++)
        {
            MountHead[] heads = mounts.Snapshot().MountHeads.ToArray();
            if (heads.Length == 0)
            {
                continue;
            }

            if (stats is null)
            {
                throw new NotSupportedException("native mounted directory reads require a directory identity/stat adapter");
            }

            byte[] original = records[index];
            ResourceIdentity identity = stats.ResolveIdentity(
                BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(2)),
                BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)),
                BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(13)));
            MountHead? head = heads.FirstOrDefault(h => h.From.Identity == identity);
            if (head is null || head.Mounts.Any(m => m.Target.Identity == identity))
            {
                continue;
            }

            byte[]? replacement = await ReplacementAsync(head.Mounts[0].Target, original);
            if (replacement is null)
            {
                continue;
            }

            while ((long)length + replacement.Length - original.Length > count)
            {
                byte[] tail = records[^1];
                rock.Add(tail); // mountrock appends evicted tails: C then B, not B then C.
                records.RemoveAt(records.Count - 1);
                length -= tail.Length;
                if (index == records.Count)
                {
                    return Join(records, length);
                }
            }

            records[index] = replacement;
            length += replacement.Length - original.Length;
        }

        return Join(records, length);
    }

    private async ValueTask<byte[]?> ReplacementAsync(ResourceHandle target, byte[] original)
    {
        try
        {
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(41));

            // A short or mismatched stat throws below, and the catch keeps the original, as mountfix does.
            ReadOnlyMemory<byte> data = await stats!.StatAsync(target, 4096, CancellationToken.None);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data.Span) + 2;
            if (size + nameLength > 4096)
            {
                data = await stats.StatAsync(target, (uint)(size + nameLength), CancellationToken.None);
            }

            DirectoryRecords.Validate(data.Span, uint.MaxValue);

            int oldNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[41..]);
            int newSize = size - oldNameLength + nameLength;
            if (newSize - 2 > ushort.MaxValue)
            {
                return null;
            }

            var result = new byte[newSize];
            data.Span[..41].CopyTo(result);
            BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)(newSize - 2));
            original.AsSpan(41, nameLength + 2).CopyTo(result.AsSpan(41));
            data.Span[(43 + oldNameLength)..].CopyTo(result.AsSpan(43 + nameLength));
            return result;
        }
        catch (Exception)
        {
        } // mountfix retains the original on stat/name conversion failure.

        return null;
    }
}

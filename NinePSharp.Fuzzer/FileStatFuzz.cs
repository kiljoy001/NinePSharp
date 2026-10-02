using System.Buffers.Binary;
using System.Text;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class FileStatFuzz
{
    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var root = new ResourceHandle(new("stat-fuzz", "root", 1), QidType.QTDIR);
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        var provider = new Mock<IFileStatOperations>(MockBehavior.Strict);
        var codec = new DirectoryStatOperations(
            new Mock<IResourceDataOperations>().Object,
            new[] { new DirectoryDeviceBinding(7, 0, "stat-fuzz", "root") });
        string name = Convert.ToHexString(data.Take(32).ToArray());
        string visible = string.Concat(data.Take(16).Select(b => b % 2 == 0 ? "é" : "z"));
        byte[] record = codec.Encode(new ResourceStat(root, name, 0x180, 17, 29, 37, "u", "g", "m"));
        bool malformed = false;
        var handle = new ResourceOpenHandle(root, "retained-stat", 0, 0);
        provider.Setup(p => p.StatAsync(handle, It.IsAny<uint>(), default))
            .Returns((ResourceOpenHandle _, uint count, CancellationToken _) =>
                ValueTask.FromResult<ReadOnlyMemory<byte>>(malformed || record.Length <= count ? record : record.AsMemory(0, 2)));
        byte[]? observedUpdate = null;
        provider.Setup(p => p.WStatAsync(handle, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns((ResourceOpenHandle _, ReadOnlyMemory<byte> stat, ResourceOperationContext _, CancellationToken _) =>
            {
                observedUpdate = stat.ToArray();
                return ValueTask.FromResult(checked((uint)stat.Length));
            });
        var calls = new Plan9FileSyscalls(
            process,
            new Mock<INamespaceDataPlane>(MockBehavior.Strict).Object,
            () => new(new("stat-fuzz", 1), process.Id, "fuzzer"),
            fileStats: provider.Object);
        int closed = 0;
        int fd = process.Descriptors.Install(
            handle,
            () =>
        {
            closed++;
            return ValueTask.CompletedTask;
        },
            visibleName: visible);
        int duplicate = await process.Descriptors.DuplicateAsync(fd);
        try
        {
            // The descriptor is directory-shaped but intentionally has no read cursor;
            // metadata must not need one or invoke the content data plane.
            foreach (uint count in data.Take(20).Select(b => (uint)b + 2).Append(4096U))
            {
                ReadOnlyMemory<byte> bytes = await calls.FStatAsync(duplicate, count);
                int expected = record.Length - Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(visible);
                int hinted = record.Length > count ? record.Length : expected;
                int length = record.Length > count || expected > count ? 2 : expected;
                if (bytes.Length != length || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span) != hinted - 2)
                {
                    throw new InvalidOperationException("incorrect bounded stat size");
                }

                if (length > 2)
                {
                    int n = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[41..]);
                    if (Encoding.UTF8.GetString(bytes.Span.Slice(43, n)) != visible
                        || !record.AsSpan(2, 39).SequenceEqual(bytes.Span.Slice(2, 39))
                        || !record.AsSpan(43 + name.Length).SequenceEqual(bytes.Span[(43 + n)..]))
                    {
                        throw new InvalidOperationException("stat rewrite corrupted name or metadata");
                    }
                }
            }

            malformed = true;
            record[0] ^= 1;
            bool rejected = false;
            try
            {
                await calls.FStatAsync(fd, 4096);
            }
            catch (IOException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("malformed stat accepted");
            }

            // Arbitrary bytes exercise exact statcheck/string/name boundaries.
            try
            {
                await calls.FWStatAsync(duplicate, data.Take(128).ToArray());
            }
            catch (NamespaceFidException)
            {
            }

            observedUpdate = null;
            byte[] update = FileStatOperations.EncodeUpdate(ResourceWStat.Unchanged() with
            {
                Mode = data.Length == 0 ? uint.MaxValue : (uint)(data[0] & 0x1ff),
                Name = Convert.ToHexString(data.Take(31).ToArray()),
            });
            uint updated = await calls.FWStatAsync(duplicate, update);
            if (updated != update.Length || observedUpdate is null || !update.SequenceEqual(observedUpdate))
            {
                throw new InvalidOperationException("valid wstat changed at the syscall boundary");
            }

            await process.Descriptors.CloseAsync(fd);
            if (closed != 0)
            {
                throw new InvalidOperationException("stat consumed duplicate ownership");
            }
        }
        finally
        {
            await table.TerminateAsync(process.Id);
        }

        if (closed != 1)
        {
            throw new InvalidOperationException("stat leaked or double-closed a descriptor");
        }
    }
}

using System.Buffers.Binary;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class DirectoryCursorFuzz
{
    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var root = new ResourceHandle(new ResourceIdentity("directory-fuzz", "root", 1), QidType.QTDIR);
        string[] names = data.Take(24).Select((value, index) => "entry" + index + new string('x', value % 12)).ToArray();
        var children = names.Select((name, index) => new ResourceDirectoryEntry(
            name,
            new ResourceHandle(new ResourceIdentity("directory-fuzz", "root", (ulong)index + 2), QidType.QTFILE))).ToArray();
        var resources = new Mock<IResourceDataOperations>(MockBehavior.Strict);
        resources.Setup(x => x.OpenAsync(root, 0, It.IsAny<ResourceOperationContext>(), default))
            .ReturnsAsync(new ResourceOpenHandle(root, "directory", 0, 0));
        resources.Setup(x => x.ReadDirectoryAsync(root, default)).ReturnsAsync(children);
        resources.Setup(x => x.StatAsync(It.IsAny<ResourceHandle>(), default))
            .Returns((ResourceHandle handle, CancellationToken _) => ValueTask.FromResult(
                new ResourceStat(handle, names[(int)handle.Identity.Path - 2], 0x180, 0, 0, 0, "u", "g", "m")));
        int clunks = 0;
        resources.Setup(x => x.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns(() =>
            {
                clunks++;
                return ValueTask.CompletedTask;
            });
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        ulong sequence = 0;
        var calls = new Plan9FileSyscalls(
            process,
            new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources.Object),
            () => new(new ResourceOperationId("directory-fuzz", ++sequence), process.Id, "fuzzer"));
        try
        {
            int fd = await calls.OpenAsync("/", new(0));
            int duplicate = await process.Descriptors.DuplicateAsync(fd);
            var actual = new List<string>();
            foreach (byte value in data.Take(32))
            {
                Decode(await calls.ReadAsync(duplicate, (uint)(80 + value)), actual);
            }

            Decode(await calls.ReadAsync(fd, 4096), actual);
            Check(names.SequenceEqual(actual), "bounded directory reads lost or reordered records");
            await calls.SeekAsync(duplicate, 0, Plan9SeekWhence.Set);
            actual.Clear();
            Decode(await calls.ReadAsync(fd, 4096), actual);
            Check(names.SequenceEqual(actual), "rewind did not restore the directory cursor");
        }
        finally
        {
            await table.TerminateAsync(process.Id);
        }

        Check(clunks == 1, "directory channel did not close exactly once");
    }

    private static void Decode(ReadOnlyMemory<byte> bytes, List<string> names)
    {
        int position = 0;
        while (position < bytes.Length)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[position..]) + 2;
            Check(length >= 49 && length <= bytes.Length - position, "split directory record");
            int consumed = 0;
            Stat record = new(bytes.Span.Slice(position, length), ref consumed);
            Check(consumed == length, "incorrect stat length");
            names.Add(record.Name);
            position += length;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

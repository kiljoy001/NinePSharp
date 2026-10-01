using System.Buffers.Binary;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class NativeDirectoryStreamFuzz
{
    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var root = new ResourceHandle(new("native-fuzz", "root", 1), QidType.QTDIR);
        var a = root with { Identity = new("native-fuzz", "root", 2) };
        var b = root with { Identity = new("native-fuzz", "root", 3) };
        var provider = new Mock<IResourceDataOperations>(MockBehavior.Strict);
        var codec = new DirectoryStatOperations(provider.Object, new[] { new DirectoryDeviceBinding(7, 0, "native-fuzz", "root") });
        string[] expected = data.Take(20).Select((value, i) => "n" + i + new string('x', value % 16)).ToArray();
        byte[][] records = expected.Select((name, i) => codec.Encode(new ResourceStat(
            new ResourceHandle(new("native-fuzz", "root", (ulong)i + 10), QidType.QTFILE), name, 0, 0, 0, 0, "u", "g", "m"))).ToArray();
        int opens = 0;
        int closes = 0;
        provider.Setup(p => p.OpenAsync(It.IsAny<ResourceHandle>(), 0, It.IsAny<ResourceOperationContext>(), default))
            .Returns((ResourceHandle resource, byte _, ResourceOperationContext context, CancellationToken _) =>
            {
                opens++;
                return ValueTask.FromResult(new ResourceOpenHandle(resource, context.OperationId.Sequence.ToString(), 0, 0));
            });
        provider.Setup(p => p.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns(() => { closes++; return ValueTask.CompletedTask; });
        provider.Setup(p => p.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), default))
            .Returns((ResourceOpenHandle handle, ulong offset, uint count, CancellationToken _) =>
            {
                byte[][] source = handle.Resource == b ? Array.Empty<byte[]>() : records;
                var output = new List<byte>();
                ulong position = 0;
                foreach (byte[] record in source)
                {
                    if (position >= offset)
                    {
                        if ((long)output.Count + record.Length > count) break;
                        output.AddRange(record);
                    }
                    position += (uint)record.Length;
                }
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(output.ToArray());
            });
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        bool union = data.Length > 0 && (data[0] & 1) != 0;
        if (union)
        {
            process.ProcessGroup.MountTable.Mount(a, root);
            process.ProcessGroup.MountTable.Mount(b, root, MountFlags.After);
        }
        ulong sequence = 0;
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, provider.Object),
            () => new(new("native-fuzz", ++sequence), process.Id, "fuzzer"), DirectoryReadMode.ProviderStream, codec);
        try
        {
            int fd = await calls.OpenAsync("/", new(0));
            int duplicate = await process.Descriptors.DuplicateAsync(fd);
            var actual = new List<string>();
            foreach (byte value in data.Take(24)) Decode(await calls.ReadAsync(duplicate, (uint)(96 + value)), actual);
            Decode(await calls.ReadAsync(fd, 4096), actual);
            if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("native stream lost or reordered records");
            await calls.SeekAsync(fd, 0, Plan9SeekWhence.Set);
            actual.Clear();
            Decode(await calls.ReadAsync(duplicate, 4096), actual);
            if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("native rewind did not restore records");
        }
        finally { await table.TerminateAsync(process.Id); }
        if (opens != closes) throw new InvalidOperationException("native member handle leak or duplicate close");
    }

    private static void Decode(ReadOnlyMemory<byte> bytes, List<string> names)
    {
        int position = 0;
        while (position < bytes.Length)
        {
            if (bytes.Length - position < 2) throw new InvalidOperationException("split directory prefix");
            int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[position..]) + 2;
            if (size < 49 || size > bytes.Length - position) throw new InvalidOperationException("split directory stat");
            int consumed = 0;
            var stat = new Stat(bytes.Span.Slice(position, size), ref consumed);
            if (consumed != size) throw new InvalidOperationException("bad directory record length");
            names.Add(stat.Name!);
            position += size;
        }
    }
}

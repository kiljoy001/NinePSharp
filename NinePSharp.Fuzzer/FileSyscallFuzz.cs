using Moq;
using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class FileSyscallFuzz
{
    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var root = new ResourceHandle(new ResourceIdentity("fuzz", "root", 1), QidType.QTDIR);
        var file = new ResourceHandle(new ResourceIdentity("fuzz", "file", 2), QidType.QTFILE);
        var table = new VProcessTable();
        VProcess process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        var resources = new Mock<IResourceDataOperations>(MockBehavior.Strict);
        long sequence = 0;
        int closed = 0;
        ulong expectedProviderOffset = 0;
        uint transfer = 0;
        resources.Setup(x => x.WalkAsync(root, "file", default)).ReturnsAsync(file);
        resources.Setup(x => x.OpenAsync(file, NinePConstants.ORDWR, It.IsAny<ResourceOperationContext>(), default))
            .ReturnsAsync(new ResourceOpenHandle(file, "fuzz-file", NinePConstants.ORDWR, 0));
        resources.Setup(x => x.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), default))
            .Returns((ResourceOpenHandle _, ulong offset, uint count, CancellationToken _) =>
            {
                Check(offset == expectedProviderOffset, "read provider offset");
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[transfer]);
            });
        resources.Setup(x => x.WriteAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns((ResourceOpenHandle _, ulong offset, ReadOnlyMemory<byte> _, ResourceOperationContext _, CancellationToken _) =>
            {
                Check(offset == expectedProviderOffset, "write provider offset");
                return ValueTask.FromResult(transfer);
            });
        resources.Setup(x => x.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns(() =>
            {
                closed++;
                return ValueTask.CompletedTask;
            });
        var calls = new Plan9FileSyscalls(
            process,
            new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources.Object),
            () => new(new ResourceOperationId("fuzz", (ulong)Interlocked.Increment(ref sequence)), process.Id, "fuzzer"));
        int fd = await calls.OpenAsync("/file", new(NinePConstants.ORDWR));
        int duplicate = await process.Descriptors.DuplicateAsync(fd);
        long modelOffset = 0;
        try
        {
            foreach (byte instruction in data.Take(128))
            {
                uint requested = (uint)(instruction % 16);
                transfer = requested / 2;
                bool positioned = (instruction & 16) != 0;
                long position = positioned ? instruction : -1;
                expectedProviderOffset = (ulong)(positioned ? position : modelOffset);
                if ((instruction & 32) != 0)
                {
                    Check(await calls.PWriteAsync(duplicate, position, new byte[requested]) == transfer, "write count");
                }
                else
                {
                    Check((await calls.PReadAsync(fd, position, requested)).Length == transfer, "read count");
                }

                if (!positioned)
                {
                    modelOffset += transfer;
                }

                Check(await calls.SeekAsync(fd, 0, Plan9SeekWhence.Current) == modelOffset, "shared position");
                Check(closed == 0, "premature provider close");
            }
        }
        finally
        {
            await table.TerminateAsync(process.Id);
        }

        Check(closed == 1, "final provider close count");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

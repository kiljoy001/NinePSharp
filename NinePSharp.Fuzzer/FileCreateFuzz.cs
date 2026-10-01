using Moq;
using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class FileCreateFuzz
{
    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var root = new ResourceHandle(new ResourceIdentity("create-fuzz", "root", 1), QidType.QTDIR);
        var files = new Dictionary<string, ResourceHandle>();
        var expected = new HashSet<string>();
        var table = new VProcessTable();
        var process = table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        var resources = new Mock<IResourceDataOperations>(MockBehavior.Strict);
        int creates = 0, truncates = 0, closes = 0;
        long sequence = 0;
        resources.Setup(x => x.WalkAsync(root, It.IsAny<string>(), default))
            .Returns((ResourceHandle _, string name, CancellationToken _) => ValueTask.FromResult(files.GetValueOrDefault(name)));
        resources.Setup(x => x.CreateAndOpenAsync(root, It.IsAny<string>(), 0x180, 2, It.IsAny<ResourceOperationContext>(), default))
            .Returns((ResourceHandle _, string name, uint _, byte mode, ResourceOperationContext context, CancellationToken _) =>
            {
                if (files.ContainsKey(name)) throw new ResourceCreateRejectedException("exists");
                var file = new ResourceHandle(new ResourceIdentity("create-fuzz", name, (ulong)files.Count + 2), QidType.QTFILE);
                files.Add(name, file);
                creates++;
                return ValueTask.FromResult(new ResourceOpenHandle(file, context.OperationId.ToString(), mode, 0));
            });
        resources.Setup(x => x.OpenAsync(It.IsAny<ResourceHandle>(), 18, It.IsAny<ResourceOperationContext>(), default))
            .Returns((ResourceHandle file, byte mode, ResourceOperationContext context, CancellationToken _) =>
            {
                truncates++;
                return ValueTask.FromResult(new ResourceOpenHandle(file, context.OperationId.ToString(), mode, 0));
            });
        resources.Setup(x => x.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), default))
            .Returns(() => { closes++; return ValueTask.CompletedTask; });
        var calls = new Plan9FileSyscalls(process, new LocalNamespaceDataPlane(process.ProcessGroup.MountTable, resources.Object),
            () => new(new ResourceOperationId("create-fuzz", (ulong)++sequence), process.Id, "fuzzer"));
        try
        {
            foreach (byte instruction in data.Take(64))
            {
                string name = "file" + (instruction % 8);
                bool exclusive = (instruction & 8) != 0;
                bool exists = expected.Contains(name);
                int oldCreates = creates, oldTruncates = truncates;
                var request = new Plan9CreateRequest(0x180, 2 | (exclusive ? NinePConstants.OEXCL : 0));
                if (exclusive && exists)
                {
                    try
                    {
                        await calls.CreateAsync(name, request);
                        throw new InvalidOperationException("exclusive create accepted an existing name");
                    }
                    catch (NamespaceException error) when (error.Error == NamespaceError.ResourceAlreadyExists) { }
                    if (creates != oldCreates || truncates != oldTruncates)
                        throw new InvalidOperationException("exclusive collision mutated a provider");
                }
                else
                {
                    int fd = await calls.CreateAsync(name, request);
                    if (fd != 0 || creates != oldCreates + (exists ? 0 : 1) || truncates != oldTruncates + (exists ? 1 : 0))
                        throw new InvalidOperationException("native create diverged from the namespace model");
                    expected.Add(name);
                    await process.Descriptors.CloseAsync(fd);
                    if (closes != creates + truncates) throw new InvalidOperationException("created handle leaked");
                }
            }
        }
        finally { await table.TerminateAsync(process.Id); }
    }
}

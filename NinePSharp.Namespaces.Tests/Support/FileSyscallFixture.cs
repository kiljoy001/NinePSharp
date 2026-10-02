using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class FileSyscallFixture : IAsyncDisposable
{
    private readonly string epoch = Guid.NewGuid().ToString();
    private long sequence;

    internal FileSyscallFixture()
    {
        var root = Resources.Directory("root", "file", "other");
        Process = Table.CreateInitial(NamespaceChannel.Restore(new[] { new ChannelFrame("/", root) }));
        Local = new LocalNamespaceDataPlane(Process.ProcessGroup.MountTable, Resources);
        Plane = new ControlledDataPlane(Local);
        Calls = ForProcess(Process);
    }

    internal MemoryDataResources Resources { get; } = new();

    internal VProcessTable Table { get; } = new();

    internal VProcess Process { get; }

    internal LocalNamespaceDataPlane Local { get; }

    internal ControlledDataPlane Plane { get; }

    internal Plan9FileSyscalls Calls { get; }

    public async ValueTask DisposeAsync()
    {
        foreach (VProcess process in Table.Snapshot())
        {
            await Table.TerminateAsync(process.Id);
        }
    }

    internal ResourceOperationContext Context()
        => new(new ResourceOperationId(epoch, (ulong)Interlocked.Increment(ref sequence)), Process.Id, "scott");

    internal Plan9FileSyscalls ForProcess(VProcess process) => new(process, Plane, Context);

    internal ValueTask<int> OpenAsync(byte mode = NinePConstants.ORDWR) => Calls.OpenAsync("/file", new(mode));
}

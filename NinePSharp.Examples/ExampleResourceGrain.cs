using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Orleans;
using NinePSharp.Namespaces.Orleans.Server;
using Orleans;
using Orleans.Hosting;

namespace NinePSharp.Examples;

/// <summary>A resource grain with a directory and one readable file; it owns no open-handle state.</summary>
public sealed class ExampleResourceGrain : Grain, IExampleResourceGrain
{
    private static readonly byte[] Contents = Encoding.UTF8.GetBytes("Hello from an Orleans grain over 9P!\n");

    public Task<ResourceHandleModel?> WalkAsync(ResourceHandleModel directory, string name)
        => Task.FromResult<ResourceHandleModel?>(directory.Identity.Path == 1 && name == "hello" ? Handle(2) : null);

    public Task<ResourceDirectoryEntryModel[]> ReadDirectoryAsync(ResourceHandleModel directory)
        => Task.FromResult(directory.Identity.Path == 1
            ? new[] { new ResourceDirectoryEntryModel("hello", Handle(2)) }
            : throw new DirectoryNotFoundException());

    public Task<ResourceOpenHandleModel> OpenAsync(ResourceHandleModel resource, byte mode, ResourceOperationContextModel context)
    {
        _ = Handle(resource.Identity.Path);
        if (mode != NinePConstants.OREAD)
        {
            throw new UnauthorizedAccessException("The example is read-only.");
        }

        return Task.FromResult(new ResourceOpenHandleModel(
            resource, $"{context.OperationId.SessionId}:{context.OperationId.Sequence}", mode, 0));
    }

    public Task<byte[]> ReadAsync(ResourceOpenHandleModel openHandle, ulong offset, uint count)
    {
        if (openHandle.Resource.Identity.Path != 2)
        {
            throw new FileNotFoundException();
        }

        int start = (int)Math.Min(offset, (ulong)Contents.Length);
        int length = (int)Math.Min(count, (uint)(Contents.Length - start));
        return Task.FromResult(Contents.AsSpan(start, length).ToArray());
    }

    public Task<ResourceStatModel> StatAsync(ResourceHandleModel resource)
    {
        ResourceHandleModel handle = Handle(resource.Identity.Path);
        bool directory = handle.Identity.Path == 1;
        return Task.FromResult(new ResourceStatModel(
            handle,
            directory ? "/" : "hello",
            directory ? (uint)NinePConstants.FileMode9P.DMDIR | 0x16DU : 0x124U,
            0,
            0,
            directory ? 0UL : (ulong)Contents.Length,
            "guest",
            "guest",
            "guest"));
    }

    public Task ClunkAsync(ResourceOpenHandleModel openHandle, ResourceOperationContextModel context) => Task.CompletedTask;

    public Task<uint> WriteAsync(ResourceOpenHandleModel handle, ulong offset, byte[] data, ResourceOperationContextModel context)
        => Task.FromException<uint>(ReadOnly());

    public Task<ResourceHandleModel> CreateAsync(ResourceHandleModel directory, string name, bool directoryEntry)
        => Task.FromException<ResourceHandleModel>(ReadOnly());

    public Task<ResourceOpenHandleModel> CreateAndOpenAsync(
        ResourceHandleModel directory, string name, uint permissions, byte mode, ResourceOperationContextModel context)
        => Task.FromException<ResourceOpenHandleModel>(ReadOnly());

    public Task RemoveAsync(ResourceHandleModel resource, ResourceOpenHandleModel? openHandle, ResourceOperationContextModel context)
        => Task.FromException(ReadOnly());

    private static UnauthorizedAccessException ReadOnly() => new("The example is read-only.");

    private ResourceHandleModel Handle(ulong path)
        => path is 1 or 2
            ? new(new ResourceIdentityModel("example", this.GetPrimaryKeyString(), path), path == 1 ? QidType.QTDIR : QidType.QTFILE, 0)
            : throw new FileNotFoundException();
}

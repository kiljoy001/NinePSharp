namespace NinePSharp.Namespaces.Orleans;

/// <summary>Resolves resource handles to the Orleans grains which expose them.</summary>
public interface IMountableResourceResolver
{
    /// <summary>Returns the grain responsible for a resource identity.</summary>
    IMountableResourceGrain Resolve(ResourceIdentityModel identity);
}

/// <summary>Routes pure namespace data operations to mountable Orleans resource grains.</summary>
public sealed class OrleansResourceOperations : IResourceDataOperations
{
    private readonly IMountableResourceResolver resolver;

    /// <summary>Initializes a new instance of the <see cref="OrleansResourceOperations"/> class.</summary>
    public OrleansResourceOperations(IMountableResourceResolver resolver)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceHandle?> WalkAsync(
        ResourceHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceHandleModel? result = await resolver.Resolve(directory.Identity.ToModel())
            .WalkAsync(directory.ToModel(), name)
            .WaitAsync(cancellationToken);
        return result?.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
        ResourceHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceDirectoryEntryModel[] result = await resolver.Resolve(directory.Identity.ToModel())
            .ReadDirectoryAsync(directory.ToModel())
            .WaitAsync(cancellationToken);
        return result.Select(entry => new ResourceDirectoryEntry(entry.Name, entry.Handle.ToDomain())).ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceHandle> CreateAsync(
        ResourceHandle directory,
        string name,
        bool directoryEntry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceHandleModel result = await resolver.Resolve(directory.Identity.ToModel())
            .CreateAsync(directory.ToModel(), name, directoryEntry)
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceOpenHandleModel result = await resolver.Resolve(resource.Identity.ToModel())
            .OpenAsync(resource.ToModel(), mode, context.ToModel())
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] result = await resolver.Resolve(openHandle.Resource.Identity.ToModel())
            .ReadAsync(openHandle.ToModel(), offset, count)
            .WaitAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await resolver.Resolve(openHandle.Resource.Identity.ToModel())
            .WriteAsync(openHandle.ToModel(), offset, data.ToArray(), context.ToModel())
            .WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceStat> StatAsync(
        ResourceHandle resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceStatModel result = await resolver.Resolve(resource.Identity.ToModel())
            .StatAsync(resource.ToModel())
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceOpenHandleModel result = await resolver.Resolve(directory.Identity.ToModel())
            .CreateAndOpenAsync(directory.ToModel(), name, permissions, mode, context.ToModel())
            .WaitAsync(cancellationToken);
        return result.ToDomain();
    }

    /// <inheritdoc/>
    public async ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await resolver.Resolve(openHandle.Resource.Identity.ToModel())
            .ClunkAsync(openHandle.ToModel(), context.ToModel())
            .WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await resolver.Resolve(resource.Identity.ToModel())
            .RemoveAsync(resource.ToModel(), openHandle?.ToModel(), context.ToModel())
            .WaitAsync(cancellationToken);
    }
}

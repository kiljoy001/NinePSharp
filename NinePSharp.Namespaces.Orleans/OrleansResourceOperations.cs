namespace NinePSharp.Namespaces.Orleans;

/// <summary>Resolves resource handles to the Orleans grains which expose them.</summary>
public interface IMountableResourceResolver
{
    /// <summary>Returns the grain responsible for a resource identity.</summary>
    IMountableResourceGrain Resolve(ResourceIdentityModel identity);
}

/// <summary>Routes pure namespace data operations to mountable Orleans resource grains.</summary>
public sealed class OrleansResourceOperations : IResourceOperations
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
}

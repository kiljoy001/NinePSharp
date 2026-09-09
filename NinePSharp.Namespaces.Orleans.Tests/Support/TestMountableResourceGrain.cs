namespace NinePSharp.Namespaces.Orleans.Tests.Support;

public sealed class TestMountableResourceGrain : Grain, IMountableResourceGrain
{
    public Task<ResourceHandleModel?> WalkAsync(ResourceHandleModel directory, string name)
    {
        ResourceHandleModel? result = directory.Identity.Path == 1 && name == "job"
            ? Handle(directory.Identity.Provider, directory.Identity.Device, 2, false)
            : null;
        return Task.FromResult(result);
    }

    public Task<ResourceDirectoryEntryModel[]> ReadDirectoryAsync(ResourceHandleModel directory)
    {
        ResourceDirectoryEntryModel[] result = directory.Identity.Path == 1
            ? new[]
            {
                new ResourceDirectoryEntryModel(
                    "job",
                    Handle(directory.Identity.Provider, directory.Identity.Device, 2, false)),
            }
            : Array.Empty<ResourceDirectoryEntryModel>();
        return Task.FromResult(result);
    }

    public Task<ResourceHandleModel> CreateAsync(
        ResourceHandleModel directory,
        string name,
        bool directoryEntry)
        => Task.FromResult(Handle(directory.Identity.Provider, directory.Identity.Device, 3, directoryEntry));

    private static ResourceHandleModel Handle(string provider, string device, ulong path, bool directory)
        => new(
            new ResourceIdentityModel(provider, device, path),
            directory ? NinePSharp.Constants.QidType.QTDIR : NinePSharp.Constants.QidType.QTFILE,
            0);
}

internal sealed class TestMountableResourceResolver : IMountableResourceResolver
{
    private readonly IGrainFactory grainFactory;

    internal TestMountableResourceResolver(IGrainFactory grainFactory)
    {
        this.grainFactory = grainFactory;
    }

    public IMountableResourceGrain Resolve(ResourceIdentityModel identity)
        => grainFactory.GetGrain<IMountableResourceGrain>(identity.Device);
}

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Maps a namespace provider name to an explicitly permitted grain interface.</summary>
public sealed class ResourceProviderRegistration
{
    private readonly Func<IGrainFactory, string, IMountableResourceGrain> resolve;

    private ResourceProviderRegistration(
        string provider,
        Func<IGrainFactory, string, IMountableResourceGrain> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        Provider = provider;
        this.resolve = resolve;
    }

    /// <summary>Gets the provider name used in resource identities.</summary>
    public string Provider { get; }

    /// <summary>Registers a typed resource interface, using the device as its string key.</summary>
    public static ResourceProviderRegistration For<TGrain>(string provider)
        where TGrain : class, IMountableResourceGrain
        => new(provider, static (factory, device) => factory.GetGrain<TGrain>(device));

    internal IMountableResourceGrain Resolve(IGrainFactory factory, string device) => resolve(factory, device);
}

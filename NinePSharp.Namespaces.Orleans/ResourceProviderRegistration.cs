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

/// <summary>Resolves only providers explicitly registered by the application.</summary>
public sealed class RegisteredMountableResourceResolver : IMountableResourceResolver
{
    private readonly IGrainFactory grainFactory;
    private readonly Dictionary<string, ResourceProviderRegistration> providers = new(StringComparer.Ordinal);

    /// <summary>Creates a resolver with an immutable provider mapping.</summary>
    public RegisteredMountableResourceResolver(
        IGrainFactory grainFactory,
        IEnumerable<ResourceProviderRegistration> registrations)
    {
        this.grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (ResourceProviderRegistration registration in registrations)
        {
            if (!providers.TryAdd(registration.Provider, registration))
            {
                throw new ArgumentException($"Duplicate resource provider '{registration.Provider}'.", nameof(registrations));
            }
        }
    }

    /// <inheritdoc/>
    public IMountableResourceGrain Resolve(ResourceIdentityModel identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Device);
        if (!providers.TryGetValue(identity.Provider, out ResourceProviderRegistration? registration))
        {
            throw new FileNotFoundException($"Unknown resource provider '{identity.Provider}'.");
        }

        return registration.Resolve(grainFactory, identity.Device);
    }
}

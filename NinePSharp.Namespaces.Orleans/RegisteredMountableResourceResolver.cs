namespace NinePSharp.Namespaces.Orleans;

/// <summary>Resolves only providers explicitly registered by the application.</summary>
public sealed class RegisteredMountableResourceResolver : IMountableResourceResolver
{
    private readonly IGrainFactory grainFactory;
    private readonly Dictionary<string, ResourceProviderRegistration> providers = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="RegisteredMountableResourceResolver"/> class. The provider mapping is immutable.</summary>
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

namespace NinePSharp.Namespaces.Orleans;

/// <summary>Resolves resource handles to the Orleans grains which expose them.</summary>
public interface IMountableResourceResolver
{
    /// <summary>Returns the grain responsible for a resource identity.</summary>
    IMountableResourceGrain Resolve(ResourceIdentityModel identity);
}

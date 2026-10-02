namespace NinePSharp.Namespaces.Orleans;

/// <summary>
/// Optional resource capability: the provider's own parent relation, for authorization layers
/// that must prove containment rather than trust a pathname.
/// </summary>
public interface IAncestryResourceGrain : IMountableResourceGrain
{
    /// <summary>Returns the resource's current parent directory, or null at the provider's root.</summary>
    Task<ResourceHandleModel?> GetParentAsync(ResourceHandleModel resource);
}

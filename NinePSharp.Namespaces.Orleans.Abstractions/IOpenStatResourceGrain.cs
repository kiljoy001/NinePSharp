namespace NinePSharp.Namespaces.Orleans;

/// <summary>Optional resource capability for metadata on an already retained open instance.</summary>
public interface IOpenStatResourceGrain : IMountableResourceGrain
{
    /// <summary>Stats the given open handle without pathname lookup or consuming it.</summary>
    Task<ResourceStatModel> StatOpenAsync(ResourceOpenHandleModel handle);
}

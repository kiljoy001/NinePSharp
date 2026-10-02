namespace NinePSharp.Namespaces.Orleans;

/// <summary>Optional resource capability for atomic path and retained-handle metadata updates.</summary>
public interface IWStatResourceGrain : IOpenStatResourceGrain
{
    /// <summary>Applies one exact-width update to a resolved resource.</summary>
    Task<uint> WStatAsync(
        ResourceHandleModel resource,
        ResourceWStatModel stat,
        ResourceOperationContextModel context);

    /// <summary>Applies one exact-width update through a retained open handle.</summary>
    Task<uint> WStatOpenAsync(
        ResourceOpenHandleModel handle,
        ResourceWStatModel stat,
        ResourceOperationContextModel context);
}

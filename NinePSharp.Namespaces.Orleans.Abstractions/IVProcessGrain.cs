namespace NinePSharp.Namespaces.Orleans;

/// <summary>Owns durable root/current channels and a reference to a vProcess group.</summary>
public interface IVProcessGrain : IGrainWithIntegerKey
{
    /// <summary>Initializes a virtual process exactly once.</summary>
    Task InitializeAsync(VProcessStateModel initialState);

    /// <summary>Returns the virtual process state.</summary>
    Task<VProcessStateModel> GetStateAsync();

    /// <summary>Changes the current directory channel.</summary>
    Task ChangeDirectoryAsync(NamespaceChannelModel currentDirectory);

    /// <summary>Creates a child with shared, copied, or empty namespace ownership.</summary>
    Task<VProcessStateModel> ForkAsync(long childProcessId, NamespaceForkModeModel mode, bool noMounts = false);

    /// <summary>Changes this process's namespace group without creating a child.</summary>
    Task<VProcessStateModel> RforkNamespaceAsync(NamespaceForkModeModel mode, bool noMounts = false);
}

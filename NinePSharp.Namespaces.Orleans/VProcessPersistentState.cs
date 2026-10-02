namespace NinePSharp.Namespaces.Orleans;

/// <summary>Persistence envelope for a virtual process.</summary>
[GenerateSerializer]
public sealed class VProcessPersistentState
{
    /// <summary>Gets or sets a value indicating whether the grain was initialized.</summary>
    [Id(0)]
    public bool Initialized { get; set; }

    /// <summary>Gets or sets the durable process state.</summary>
    [Id(1)]
    public VProcessStateModel? Process { get; set; }
}

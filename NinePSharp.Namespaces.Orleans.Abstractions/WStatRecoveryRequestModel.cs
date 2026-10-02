using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Orleans;

/// <summary>The exact target and payload saved before a wstat provider dispatch.</summary>
[GenerateSerializer]
public sealed record WStatRecoveryRequestModel(
    [property: Id(0)] ResourceOperationContextModel Context,
    [property: Id(1)] ResourceHandleModel Resource,
    [property: Id(2)] ResourceOpenHandleModel? OpenHandle,
    [property: Id(3)] byte[] Stat,
    [property: Id(4)] string Fingerprint);

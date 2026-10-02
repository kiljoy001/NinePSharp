using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

/// <summary>Selects the directory contract implemented by the configured providers.</summary>
public enum DirectoryReadMode
{
    /// <summary>Adapt whole metadata listings into a snapshot cursor.</summary>
    Metadata,

    /// <summary>Read complete 9P2000 stat records through retained provider handles.</summary>
    ProviderStream,
}

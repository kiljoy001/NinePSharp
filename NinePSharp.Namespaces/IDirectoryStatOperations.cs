using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>Maps native directory identities and performs bounded mounted-entry stat.</summary>
public interface IDirectoryStatOperations
{
    /// <summary>Resolves wire type/device/path without treating the Qid version as identity.</summary>
    ResourceIdentity ResolveIdentity(ushort type, uint device, ulong path);

    /// <summary>Returns a complete stat or its two-byte required-size prefix when count is too small.</summary>
    ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken);
}

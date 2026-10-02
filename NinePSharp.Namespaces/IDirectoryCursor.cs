using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

internal interface IDirectoryCursor
{
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, uint count, CancellationToken cancellationToken, MountTable callingNamespace);

    ValueTask RewindAsync(CancellationToken cancellationToken);

    ValueTask CloseAsync() => ValueTask.CompletedTask;
}

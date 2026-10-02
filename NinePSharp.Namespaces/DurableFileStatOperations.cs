using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>
/// Decorates raw metadata operations with durable admission, exact request replay,
/// and terminal outcome recording.
/// </summary>
public sealed class DurableFileStatOperations : IFileStatOperations
{
    private readonly IFileStatOperations inner;
    private readonly IWStatRecoveryStore store;

    public DurableFileStatOperations(IFileStatOperations inner, IWStatRecoveryStore store)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
        => inner.StatAsync(resource, count, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken)
        => inner.StatAsync(handle, count, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(
        ResourceHandle resource,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => AdmitAndExecuteAsync(WStatRecoveryRequest.ForResource(resource, stat, context), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(
        ResourceOpenHandle handle,
        ReadOnlyMemory<byte> stat,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
        => AdmitAndExecuteAsync(WStatRecoveryRequest.ForOpenHandle(handle, stat, context), cancellationToken);

    /// <summary>Resolves one pending operation without repeating namespace or descriptor lookup.</summary>
    public async ValueTask<uint> RecoverAsync(ResourceOperationId operationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        WStatRecoveryRecord record = await store.GetAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException("The wstat recovery operation does not exist.");
        return record.State switch
        {
            WStatRecoveryState.Committed => record.Result
                ?? throw new InvalidDataException("A committed wstat operation has no result."),
            WStatRecoveryState.Rejected => throw new ResourceWStatRejectedException(
                record.Error ?? "wstat rejected"),
            WStatRecoveryState.Pending => await DispatchAndRecordAsync(record.Request),
            _ => throw new InvalidDataException("Unknown wstat recovery state."),
        };
    }

    /// <summary>Attempts every pending operation for a session using each saved target and identity.</summary>
    public async ValueTask<int> RecoverPendingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WStatRecoveryRecord> pending = await store.GetPendingAsync(sessionId, cancellationToken);
        int resolved = 0;
        foreach (WStatRecoveryRecord record in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DispatchAndRecordAsync(record.Request);
                resolved++;
            }
            catch (ResourceWStatRejectedException)
            {
                resolved++;
            }
            catch (WStatRecoveryPendingException)
            {
                // Leave this intent pending and continue reconciling independent operations.
            }
        }

        return resolved;
    }

    private async ValueTask<uint> AdmitAndExecuteAsync(WStatRecoveryRequest request, CancellationToken cancellationToken)
    {
        WStatRecoveryRecord record = await store.BeginAsync(request, cancellationToken);
        if (!string.Equals(record.Request.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The recovery store returned a different wstat request.");
        }

        return record.State switch
        {
            WStatRecoveryState.Committed => record.Result
                ?? throw new InvalidDataException("A committed wstat operation has no result."),
            WStatRecoveryState.Rejected => throw new ResourceWStatRejectedException(
                record.Error ?? "wstat rejected"),
            WStatRecoveryState.Pending => await DispatchAndRecordAsync(record.Request),
            _ => throw new InvalidDataException("Unknown wstat recovery state."),
        };
    }

    private async ValueTask<uint> DispatchAndRecordAsync(WStatRecoveryRequest request)
    {
        try
        {
            uint result = request.OpenHandle is null
                ? await inner.WStatAsync(request.Resource, request.Stat, request.Context, CancellationToken.None)
                : await inner.WStatAsync(request.OpenHandle, request.Stat, request.Context, CancellationToken.None);
            await store.CommitAsync(request.Context.OperationId, request.Fingerprint, result, CancellationToken.None);
            return result;
        }
        catch (ResourceWStatRejectedException rejected)
        {
            try
            {
                await store.RejectAsync(
                    request.Context.OperationId,
                    request.Fingerprint,
                    rejected.Message,
                    CancellationToken.None);
            }
            catch (Exception journalFailure)
            {
                throw new WStatRecoveryPendingException(request.Context, journalFailure);
            }

            throw;
        }
        catch (WStatRecoveryPendingException)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw new WStatRecoveryPendingException(request.Context, failure);
        }
    }
}

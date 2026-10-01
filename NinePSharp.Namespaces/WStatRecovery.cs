using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>The durable resolution state of one admitted metadata mutation.</summary>
public enum WStatRecoveryState
{
    /// <summary>The request may or may not have reached its provider.</summary>
    Pending,

    /// <summary>The provider acknowledged the mutation and its result is durable.</summary>
    Committed,

    /// <summary>The provider definitively rejected the mutation without applying it.</summary>
    Rejected,
}

/// <summary>A definite provider rejection which guarantees that the requested mutation was not applied.</summary>
public sealed class ResourceWStatRejectedException(string message) : IOException(message);

/// <summary>An admitted mutation whose provider outcome must be reconciled under its original identity.</summary>
public sealed class WStatRecoveryPendingException(ResourceOperationContext context, Exception inner)
    : IOException("wstat outcome is unknown; reconcile the original operation before retrying", inner)
{
    /// <summary>Gets the identity and principal of the unresolved operation.</summary>
    public ResourceOperationContext Context { get; } = context;
}

/// <summary>An immutable path or retained-handle mutation saved before provider dispatch.</summary>
public sealed class WStatRecoveryRequest
{
    private readonly byte[] stat;

    private WStatRecoveryRequest(ResourceOperationContext context, ResourceHandle resource,
        ResourceOpenHandle? openHandle, ReadOnlyMemory<byte> stat)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        OpenHandle = openHandle;
        this.stat = stat.ToArray();
        Fingerprint = FingerprintOf(context, resource, openHandle, this.stat);
    }

    /// <summary>Gets the original authenticated operation context.</summary>
    public ResourceOperationContext Context { get; }

    /// <summary>Gets the stable resource selected before dispatch.</summary>
    public ResourceHandle Resource { get; }

    /// <summary>Gets the retained provider handle for fwstat, or null for path wstat.</summary>
    public ResourceOpenHandle? OpenHandle { get; }

    /// <summary>Gets an immutable view of the exact validated raw request.</summary>
    public ReadOnlyMemory<byte> Stat => stat;

    /// <summary>Gets the canonical request fingerprint used to reject operation-ID reuse.</summary>
    public string Fingerprint { get; }

    /// <summary>Creates a saved path-wstat request after namespace selection.</summary>
    public static WStatRecoveryRequest ForResource(ResourceHandle resource, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context)
        => new(context, resource, null, stat);

    /// <summary>Creates a saved fwstat request using its retained provider handle.</summary>
    public static WStatRecoveryRequest ForOpenHandle(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new(context, handle.Resource, handle, stat);
    }

    private static string FingerprintOf(ResourceOperationContext context, ResourceHandle resource,
        ResourceOpenHandle? openHandle, byte[] stat)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8);
        writer.Write(context.ProcessId);
        writer.Write(context.User);
        writer.Write(resource.Identity.Provider);
        writer.Write(resource.Identity.Device);
        writer.Write(resource.Identity.Path);
        writer.Write((byte)resource.Type);
        writer.Write(resource.Version);
        writer.Write(openHandle is not null);
        if (openHandle is not null)
        {
            writer.Write(openHandle.HandleId);
            writer.Write(openHandle.Mode);
            writer.Write(openHandle.IoUnit);
            writer.Write(openHandle.IsMountTransport);
        }
        writer.Write(stat.Length);
        writer.Write(stat);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
    }
}

/// <summary>A durable journal entry containing the request and its current provider outcome.</summary>
public sealed record WStatRecoveryRecord(
    WStatRecoveryRequest Request,
    WStatRecoveryState State,
    uint? Result = null,
    string? Error = null);

/// <summary>Persists metadata mutation intents and terminal outcomes across host failure.</summary>
public interface IWStatRecoveryStore
{
    /// <summary>Creates an intent or returns the matching existing operation.</summary>
    ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken);

    /// <summary>Returns one operation by its stable identity.</summary>
    ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken);

    /// <summary>Returns unresolved operations for one session epoch.</summary>
    ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Persists the provider result if the fingerprint still identifies the admitted request.</summary>
    ValueTask CommitAsync(ResourceOperationId operationId, string fingerprint, uint result,
        CancellationToken cancellationToken);

    /// <summary>Persists a definite provider rejection without applying or retrying the mutation.</summary>
    ValueTask RejectAsync(ResourceOperationId operationId, string fingerprint, string error,
        CancellationToken cancellationToken);
}

/// <summary>An in-process journal useful for local hosts and deterministic tests.</summary>
public sealed class MemoryWStatRecoveryStore : IWStatRecoveryStore
{
    private readonly object gate = new();
    private readonly Dictionary<ResourceOperationId, WStatRecoveryRecord> records = new();

    /// <inheritdoc/>
    public ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ResourceOperationId id = request.Context.OperationId;
            if (records.TryGetValue(id, out WStatRecoveryRecord? existing))
            {
                EnsureFingerprint(existing, request.Fingerprint);
                return ValueTask.FromResult(existing);
            }
            var created = new WStatRecoveryRecord(request, WStatRecoveryState.Pending);
            records.Add(id, created);
            return ValueTask.FromResult(created);
        }
    }

    /// <inheritdoc/>
    public ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            records.TryGetValue(operationId, out WStatRecoveryRecord? record);
            return ValueTask.FromResult(record);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IReadOnlyList<WStatRecoveryRecord> pending = records.Values
                .Where(record => record.State == WStatRecoveryState.Pending
                    && record.Request.Context.OperationId.SessionId == sessionId)
                .OrderBy(record => record.Request.Context.OperationId.Sequence)
                .ToArray();
            return ValueTask.FromResult(pending);
        }
    }

    /// <inheritdoc/>
    public ValueTask CommitAsync(ResourceOperationId operationId, string fingerprint, uint result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            WStatRecoveryRecord record = Require(operationId, fingerprint);
            if (record.State == WStatRecoveryState.Rejected)
                throw new InvalidOperationException("A rejected wstat operation cannot be committed.");
            if (record.State == WStatRecoveryState.Committed)
            {
                if (record.Result != result)
                    throw new InvalidOperationException("A committed wstat operation cannot change its result.");
                return ValueTask.CompletedTask;
            }
            records[operationId] = record with { State = WStatRecoveryState.Committed, Result = result, Error = null };
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc/>
    public ValueTask RejectAsync(ResourceOperationId operationId, string fingerprint, string error,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            WStatRecoveryRecord record = Require(operationId, fingerprint);
            if (record.State == WStatRecoveryState.Committed)
                throw new InvalidOperationException("A committed wstat operation cannot be rejected.");
            if (record.State == WStatRecoveryState.Rejected)
            {
                if (!string.Equals(record.Error, error, StringComparison.Ordinal))
                    throw new InvalidOperationException("A rejected wstat operation cannot change its error.");
                return ValueTask.CompletedTask;
            }
            records[operationId] = record with { State = WStatRecoveryState.Rejected, Result = null, Error = error };
            return ValueTask.CompletedTask;
        }
    }

    private WStatRecoveryRecord Require(ResourceOperationId operationId, string fingerprint)
    {
        if (!records.TryGetValue(operationId, out WStatRecoveryRecord? record))
            throw new KeyNotFoundException("The wstat operation was not durably admitted.");
        EnsureFingerprint(record, fingerprint);
        return record;
    }

    private static void EnsureFingerprint(WStatRecoveryRecord record, string fingerprint)
    {
        if (!string.Equals(record.Request.Fingerprint, fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("An operation identity was reused for a different wstat request.");
    }
}

/// <summary>
/// Decorates raw metadata operations with durable admission, exact request replay,
/// and terminal outcome recording.
/// </summary>
public sealed class DurableFileStatOperations : IFileStatOperations
{
    private readonly IFileStatOperations inner;
    private readonly IWStatRecoveryStore store;

    /// <summary>Creates durable operations around an existing raw stat provider.</summary>
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
    public ValueTask<uint> WStatAsync(ResourceHandle resource, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
        => AdmitAndExecuteAsync(WStatRecoveryRequest.ForResource(resource, stat, context), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<uint> WStatAsync(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
        ResourceOperationContext context, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("The recovery store returned a different wstat request.");
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
                await store.RejectAsync(request.Context.OperationId, request.Fingerprint,
                    rejected.Message, CancellationToken.None);
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

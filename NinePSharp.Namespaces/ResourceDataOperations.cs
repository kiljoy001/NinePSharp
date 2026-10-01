using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Identifies one request independently of a reusable 9P tag.</summary>
public sealed record ResourceOperationId
{
    /// <summary>Initializes a request identity.</summary>
    public ResourceOperationId(string sessionId, ulong sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Operation sequences start at one.");
        }

        SessionId = sessionId;
        Sequence = sequence;
    }

    /// <summary>Gets the connection-local session epoch.</summary>
    public string SessionId { get; }

    /// <summary>Gets the monotonically increasing request sequence.</summary>
    public ulong Sequence { get; }
}

/// <summary>Security and process context propagated with a resource operation.</summary>
public sealed record ResourceOperationContext
{
    /// <summary>Initializes an operation context.</summary>
    public ResourceOperationContext(ResourceOperationId operationId, long processId, string user)
    {
        OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ProcessId = processId;
        User = user;
    }

    /// <summary>Gets the stable identity used for idempotency.</summary>
    public ResourceOperationId OperationId { get; }

    /// <summary>Gets the virtual process issuing the operation.</summary>
    public long ProcessId { get; }

    /// <summary>Gets the authenticated Plan 9 user.</summary>
    public string User { get; }
}

/// <summary>Provider-owned state associated with an open fid.</summary>
public sealed record ResourceOpenHandle
{
    /// <summary>Initializes and validates an open resource handle.</summary>
    public ResourceOpenHandle(ResourceHandle resource, string handleId, byte mode, uint ioUnit, bool isMountTransport = false)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        ArgumentException.ThrowIfNullOrWhiteSpace(handleId);
        HandleId = handleId;
        Mode = mode;
        IoUnit = ioUnit;
        IsMountTransport = isMountTransport;
    }

    /// <summary>Gets the opened resource.</summary>
    public ResourceHandle Resource { get; }

    /// <summary>Gets the provider-owned open-handle identity.</summary>
    public string HandleId { get; }

    /// <summary>Gets the Plan 9 open mode.</summary>
    public byte Mode { get; }

    /// <summary>Gets the maximum provider-specific I/O payload, or zero for the connection default.</summary>
    public uint IoUnit { get; }

    /// <summary>Gets whether this is the native mount protocol transport channel (CMSG).</summary>
    public bool IsMountTransport { get; }
}

/// <summary>Provider-neutral metadata needed to produce a 9P stat response.</summary>
public sealed record ResourceStat(
    ResourceHandle Resource,
    string Name,
    uint Mode,
    uint AccessTime,
    uint ModificationTime,
    ulong Length,
    string User,
    string Group,
    string LastModifier);

/// <summary>
/// One decoded Plan 9 wstat request. Width-specific all-ones values and empty
/// strings retain their native unchanged-field meaning.
/// </summary>
public sealed record ResourceWStat(
    ushort Type,
    uint Device,
    Qid Qid,
    uint Mode,
    uint AccessTime,
    uint ModificationTime,
    ulong Length,
    string Name,
    string User,
    string Group,
    string LastModifier,
    uint EncodedLength)
{
    /// <summary>Creates the native nulldir request in which every field is unchanged.</summary>
    public static ResourceWStat Unchanged(uint encodedLength = 49)
        => new(
            ushort.MaxValue,
            uint.MaxValue,
            new Qid((QidType)byte.MaxValue, uint.MaxValue, ulong.MaxValue),
            uint.MaxValue,
            uint.MaxValue,
            uint.MaxValue,
            ulong.MaxValue,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            encodedLength);
}

/// <summary>Extends namespace traversal with stateful file operations.</summary>
public interface IResourceDataOperations : IResourceOperations
{
    /// <summary>Opens a resource and returns provider-owned open state.</summary>
    ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads bytes from an open resource.</summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        uint count,
        CancellationToken cancellationToken);

    /// <summary>Writes bytes to an open resource.</summary>
    ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Reads metadata for a resource.</summary>
    ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken);

    /// <summary>
    /// Creates and opens a previously absent child atomically (9P create(5)). An
    /// existing name must fail without truncation. Replay must return the original
    /// handle. Use ResourceCreateRejectedException only for a definite rejection;
    /// ordinary IO/transport failures must never imply that creation did not commit.
    /// Native create(2) lookup/truncate behavior belongs to Plan9FileSyscalls.
    /// </summary>
    ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Closes provider-owned open state.</summary>
    ValueTask ClunkAsync(
        ResourceOpenHandle openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Removes a resource. The caller invalidates its fid regardless of the result.</summary>
    ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken);
}

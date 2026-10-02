using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Identifies one request independently of a reusable 9P tag.</summary>
public sealed record ResourceOperationId
{
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

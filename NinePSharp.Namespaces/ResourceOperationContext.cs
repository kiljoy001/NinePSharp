using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Namespaces;

/// <summary>Security and process context propagated with a resource operation.</summary>
public sealed record ResourceOperationContext
{
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

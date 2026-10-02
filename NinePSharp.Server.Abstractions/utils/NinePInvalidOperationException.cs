using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Thrown when an operation is invalid in the current state.
/// Maps to EINVAL (22) in Linux.
/// </summary>
public class NinePInvalidOperationException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePInvalidOperationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePInvalidOperationException(string message = "Invalid operation")
        : base(message, 22)
    {
    }
}

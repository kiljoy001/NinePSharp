using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Thrown when an operation is not supported by the backend.
/// Maps to EOPNOTSUPP (95) in Linux.
/// </summary>
public class NinePNotSupportedException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePNotSupportedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePNotSupportedException(string message = "Operation not supported")
        : base(message, 95)
    {
    }
}

using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Thrown when permission is denied.
/// Maps to EACCES (13) in Linux.
/// </summary>
public class NinePPermissionDeniedException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePPermissionDeniedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePPermissionDeniedException(string message = "Permission denied")
        : base(message, 13)
    {
    }
}

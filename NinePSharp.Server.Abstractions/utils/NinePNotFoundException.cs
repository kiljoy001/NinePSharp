using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Thrown when a file or directory is not found.
/// Maps to ENOENT (2) in Linux.
/// </summary>
public class NinePNotFoundException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePNotFoundException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePNotFoundException(string message = "File not found")
        : base(message, 2)
    {
    }
}

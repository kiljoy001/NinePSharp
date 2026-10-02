using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Base exception for 9P protocol-related errors.
/// </summary>
public class NinePProtocolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NinePProtocolException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The numeric error code (defaults to EIO).</param>
    public NinePProtocolException(string message, int errorCode = 5)
        : base(message) // Default to EIO
    {
        ErrorMessage = message;
        ErrorCode = errorCode;
    }

    /// <summary>Gets the error message.</summary>
    public string ErrorMessage { get; }

    /// <summary>Gets the platform-specific error code.</summary>
    public int ErrorCode { get; }
}

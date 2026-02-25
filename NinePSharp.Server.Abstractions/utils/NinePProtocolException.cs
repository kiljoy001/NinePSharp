using System;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Base exception for 9P protocol-related errors.
/// </summary>
public class NinePProtocolException : Exception
{
    /// <summary>Gets the error message.</summary>
    public string ErrorMessage { get; }
    /// <summary>Gets the platform-specific error code.</summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NinePProtocolException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The numeric error code (defaults to EIO).</param>
    public NinePProtocolException(string message, int errorCode = 5) : base(message) // Default to EIO
    {
        ErrorMessage = message;
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when an operation is not supported by the backend.
/// Maps to EOPNOTSUPP (95) in Linux.
/// </summary>
public class NinePNotSupportedException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePNotSupportedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePNotSupportedException(string message = "Operation not supported") 
        : base(message, 95) { }
}

/// <summary>
/// Thrown when permission is denied.
/// Maps to EACCES (13) in Linux.
/// </summary>
public class NinePPermissionDeniedException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePPermissionDeniedException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePPermissionDeniedException(string message = "Permission denied") 
        : base(message, 13) { }
}

/// <summary>
/// Thrown when a file or directory is not found.
/// Maps to ENOENT (2) in Linux.
/// </summary>
public class NinePNotFoundException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePNotFoundException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePNotFoundException(string message = "File not found") 
        : base(message, 2) { }
}

/// <summary>
/// Thrown when an operation is invalid in the current state.
/// Maps to EINVAL (22) in Linux.
/// </summary>
public class NinePInvalidOperationException : NinePProtocolException
{
    /// <summary>Initializes a new instance of the <see cref="NinePInvalidOperationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public NinePInvalidOperationException(string message = "Invalid operation") 
        : base(message, 22) { }
}

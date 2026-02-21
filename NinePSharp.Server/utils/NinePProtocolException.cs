using System;

namespace NinePSharp.Server.Utils;

public class NinePProtocolException : Exception
{
    public string ErrorMessage { get; }
    public int? ErrorCode { get; }

    public NinePProtocolException(string message, int? errorCode = null) : base(message)
    {
        ErrorMessage = message;
        ErrorCode = errorCode;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>A durable journal entry containing the request and its current provider outcome.</summary>
public sealed record WStatRecoveryRecord(
    WStatRecoveryRequest Request,
    WStatRecoveryState State,
    uint? Result = null,
    string? Error = null);

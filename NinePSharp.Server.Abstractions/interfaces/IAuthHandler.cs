using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Handles authentication handshakes for 9P sessions.
/// </summary>
public interface IAuthHandler
{
    /// <summary>
    /// Read from the auth channel.
    /// </summary>
    Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct);

    /// <summary>
    /// Write to the auth channel.
    /// </summary>
    Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct);
}

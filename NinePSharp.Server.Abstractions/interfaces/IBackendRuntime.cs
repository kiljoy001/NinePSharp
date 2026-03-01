using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Core 9P2000 backend runtime interface.
/// Strictly implements the Plan 9 9P2000 kernel model without .u or .L extensions.
/// </summary>
public interface IBackendRuntime
{
    string Id { get; }

    string MountPath { get; }

    NinePDialect Dialect { get; set; }

    Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect);

    Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect);

    Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default);

    Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default);

    Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect);

    Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect);

    Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect);

    Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect);

    Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect);
}

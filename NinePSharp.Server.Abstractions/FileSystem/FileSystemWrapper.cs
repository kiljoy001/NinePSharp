using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.FileSystem;

internal class FileSystemWrapper : INinePFileSystem, IReaddirCapableBackendRuntime, INinePRequestHandler
{
    private readonly FileSystemBackend inner;

    public FileSystemWrapper(FileSystemBackend inner) => this.inner = inner;

    public string Id => inner.Id;

    public string MountPath => inner.MountPath;

    public NinePDialect Dialect { get => inner.Dialect; set => inner.Dialect = value; }

    public Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => inner.GetAuthHandlerAsync(msg, ct);

    public Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct) => inner.AttachAsync(msg, ct);

    public Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct) => inner.WalkAsync(relativePath, msg, ct);

    public Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct) => inner.OpenAsync(relativePath, msg, ct);

    public Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct) => inner.ReadAsync(relativePath, msg, ct);

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct) => inner.WriteAsync(relativePath, msg, ct);

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct) => inner.ClunkAsync(relativePath, msg, ct);

    public Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct) => inner.StatAsync(relativePath, msg, ct);

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct) => inner.WstatAsync(relativePath, msg, ct);

    public Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct) => inner.CreateAsync(parentPath, msg, ct);

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct) => inner.RemoveAsync(relativePath, msg, ct);

    public Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct) => inner.ReaddirAsync(relativePath, msg, ct);

    public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect) => inner.WalkAsync(relativePath, dialect);

    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect, CancellationToken ct = default) => inner.OpenAsync(relativePath, topen, dialect, ct);

    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default) => inner.ReadAsync(relativePath, tread, dialect, ct);

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default) => inner.WriteAsync(relativePath, twrite, dialect, ct);

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect) => inner.ClunkAsync(relativePath, tclunk, dialect);

    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect, CancellationToken ct = default) => inner.StatAsync(relativePath, tstat, dialect, ct);

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect, CancellationToken ct = default) => inner.WstatAsync(relativePath, twstat, dialect, ct);

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect, CancellationToken ct = default) => inner.RemoveAsync(relativePath, tremove, dialect, ct);

    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect, CancellationToken ct = default) => inner.CreateAsync(parentRelativePath, tcreate, dialect, ct);

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => inner.ReaddirAsync(relativePath, treaddir, dialect, ct);

    public Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => inner.ReaddirCompatAsync(relativePath, treaddir, dialect, ct);

    public Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => inner.SymlinkAsync(relativePath, msg, ct);

    public Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => inner.ReadlinkAsync(relativePath, msg, ct);

    public Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => inner.LinkAsync(relativePath, msg, ct);

    public Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => inner.LockAsync(relativePath, msg, ct);

    public Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => inner.GetlockAsync(relativePath, msg, ct);

    public Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => inner.XattrwalkAsync(relativePath, msg, ct);

    public Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => inner.XattrcreateAsync(relativePath, msg, ct);

    public Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => inner.FlushAsync(msg, ct);
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests.Helpers;

internal abstract class TestHandlerBase : INinePRequestHandler, INinePFileSystem
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string MountPath { get; set; } = "/";

    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public virtual Task<IAuthHandler?> GetAuthHandlerAsync(Tauth msg, CancellationToken ct) => Task.FromResult<IAuthHandler?>(null);

    public virtual Task<Rattach> AttachAsync(Tattach msg, CancellationToken ct) => Task.FromResult(new Rattach(msg.Tag, new Qid(QidType.QTDIR, 0, 0)));

    public abstract Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct);

    public abstract Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct);

    public abstract Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct);

    public abstract Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct);

    public virtual Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk msg, CancellationToken ct) => Task.FromResult(new Rclunk(msg.Tag));

    public abstract Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct);

    public virtual Task<Rwstat> WstatAsync(string[] relativePath, Twstat msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rcreate> CreateAsync(string[] parentPath, Tcreate msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rremove> RemoveAsync(string[] relativePath, Tremove msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rreaddir>? ReaddirAsync(string[] relativePath, Treaddir msg, CancellationToken ct)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0644, 0, 0, 0, "test", "root", "root", "root", NinePDialect.NineP2000L);
        var buffer = new byte[stat.Size];
        int off = 0;
        stat.WriteTo(buffer, ref off);
        return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + buffer.Length), msg.Tag, (uint)buffer.Length, new ReadOnlyMemory<byte>(buffer)));
    }

    public virtual Task<Rsymlink> SymlinkAsync(string[] relativePath, Tsymlink msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rreadlink> ReadlinkAsync(string[] relativePath, Treadlink msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rlink> LinkAsync(string[] relativePath, Tlink msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rlerror> LockAsync(string[] relativePath, Tlock msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rgetlock> GetlockAsync(string[] relativePath, Tgetlock msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rxattrwalk> XattrwalkAsync(string[] relativePath, Txattrwalk msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rxattrcreate> XattrcreateAsync(string[] relativePath, Txattrcreate msg, CancellationToken ct) => throw new NotImplementedException();

    public virtual Task<Rflush> FlushAsync(Tflush msg, CancellationToken ct) => Task.FromResult(new Rflush(msg.Tag));

    public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect) => throw new NotImplementedException();

    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect) => throw new NotImplementedException();

    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<Rreaddir> ReaddirCompatAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default) => throw new NotImplementedException();
}

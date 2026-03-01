using System;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Abstractions.Utils;

public sealed class FileSystemBackendRuntime : IBackendRuntime, IReaddirCapableBackendRuntime
{
    private readonly Func<INinePFileSystem> _createSession;

    public FileSystemBackendRuntime(string id, string mountPath, Func<INinePFileSystem> createSession)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(mountPath);
        ArgumentNullException.ThrowIfNull(createSession);

        Id = id;
        MountPath = mountPath;
        _createSession = createSession;
    }

        public string Id { get; }
        public string MountPath { get; }
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;
    
        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
    
        => WithFreshFileSystemAsync(dialect, fs => fs.WalkAsync(new Twalk(0, 0, 0, relativePath)));

    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, fs => fs.OpenAsync(topen));

    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return WithFileSystemAsync(relativePath, dialect, fs => fs.ReadAsync(tread));
    }

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return WithFileSystemAsync(relativePath, dialect, fs => fs.WriteAsync(twrite));
    }

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, fs => fs.ClunkAsync(tclunk));

    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, fs => fs.StatAsync(tstat));

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, fs => fs.WstatAsync(twstat));

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, fs => fs.RemoveAsync(tremove));

    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
        => WithFileSystemAsync(parentRelativePath, dialect, fs => fs.CreateAsync(tcreate));

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect)
        => WithFileSystemAsync(relativePath, dialect, async fs =>
        {
            if (fs is IReaddirCapableFileSystem readdirFileSystem)
            {
                return await readdirFileSystem.ReaddirAsync(treaddir);
            }

            var read = await fs.ReadAsync(new Tread(treaddir.Tag, treaddir.Fid, treaddir.Offset, treaddir.Count));
            return new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + read.Data.Length), treaddir.Tag, read.Count, read.Data);
        });

    private async Task<T> WithFileSystemAsync<T>(string[] relativePath, NinePDialect dialect, Func<INinePFileSystem, Task<T>> action)
    {
        return await WithFreshFileSystemAsync(dialect, async fs =>
        {
            if (relativePath.Length > 0)
            {
                var walk = await fs.WalkAsync(new Twalk(0, 0, 0, relativePath));
                if (walk.Wqid == null || walk.Wqid.Length != relativePath.Length)
                {
                    throw new NinePProtocolException("Resolved backend path no longer exists");
                }
            }

            return await action(fs);
        });
    }

    private async Task<T> WithFreshFileSystemAsync<T>(NinePDialect dialect, Func<INinePFileSystem, Task<T>> action)
    {
        var fs = _createSession();
        fs.Dialect = dialect;
        return await action(fs);
    }
}

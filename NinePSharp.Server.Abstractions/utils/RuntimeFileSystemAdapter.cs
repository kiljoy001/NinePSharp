using System;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Adapts an IBackendRuntime back to INinePFileSystem for legacy consumers.
/// This allows existing session-based code to work with the new path-based runtimes.
/// </summary>
public sealed class RuntimeFileSystemAdapter : INinePFileSystem, IBackendRuntime, IReaddirCapableBackendRuntime
{
    private readonly IBackendRuntime? _runtime;
    private readonly INinePFileSystem? _fs;
    private readonly string[] _basePath;

    public RuntimeFileSystemAdapter(IBackendRuntime runtime, string[]? basePath = null)
    {
        _runtime = runtime;
        _basePath = basePath ?? Array.Empty<string>();
    }

    public RuntimeFileSystemAdapter(INinePFileSystem fs, string[]? basePath = null)
    {
        _fs = fs;
        _basePath = basePath ?? Array.Empty<string>();
    }

    public string Id => _runtime?.Id ?? "adapted-fs";
    public string MountPath => _runtime?.MountPath ?? "/";

    public NinePDialect Dialect 
    { 
        get => _runtime?.Dialect ?? _fs?.Dialect ?? NinePDialect.NineP2000;
        set { if (_runtime != null) _runtime.Dialect = value; if (_fs != null) _fs.Dialect = value; }
    }

    NinePDialect IBackendRuntime.Dialect { get => Dialect; set => Dialect = value; }

    public Task<Rwalk> WalkAsync(Twalk twalk) 
        => _runtime != null ? _runtime.WalkAsync(Concat(_basePath, twalk.Wname), Dialect) : _fs!.WalkAsync(twalk);

    public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
        => _runtime != null ? _runtime.WalkAsync(relativePath, dialect) : WalkOnFreshFileSystemAsync(relativePath, dialect);

    public Task<Ropen> OpenAsync(Topen topen) 
        => _runtime != null ? _runtime.OpenAsync(_basePath, topen, Dialect) : _fs!.OpenAsync(topen);

    public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
        => _runtime != null ? _runtime.OpenAsync(relativePath, topen, dialect) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.OpenAsync(topen));

    public Task<Rread> ReadAsync(Tread tread)
        => _runtime != null ? _runtime.ReadAsync(_basePath, tread, Dialect) : _fs!.ReadAsync(tread);

    public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _runtime != null ? _runtime.ReadAsync(relativePath, tread, dialect, ct) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.ReadAsync(tread));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
        => _runtime != null ? _runtime.WriteAsync(_basePath, twrite, Dialect) : _fs!.WriteAsync(twrite);

    public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _runtime != null ? _runtime.WriteAsync(relativePath, twrite, dialect, ct) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.WriteAsync(twrite));
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) 
        => _runtime != null ? _runtime.ClunkAsync(_basePath, tclunk, Dialect) : _fs!.ClunkAsync(tclunk);

    public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
        => _runtime != null ? _runtime.ClunkAsync(relativePath, tclunk, dialect) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.ClunkAsync(tclunk));

    public Task<Rstat> StatAsync(Tstat tstat) 
        => _runtime != null ? _runtime.StatAsync(_basePath, tstat, Dialect) : _fs!.StatAsync(tstat);

    public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
        => _runtime != null ? _runtime.StatAsync(relativePath, tstat, dialect) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.StatAsync(tstat));

    public Task<Rwstat> WstatAsync(Twstat twstat) 
        => _runtime != null ? _runtime.WstatAsync(_basePath, twstat, Dialect) : _fs!.WstatAsync(twstat);

    public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
        => _runtime != null ? _runtime.WstatAsync(relativePath, twstat, dialect) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.WstatAsync(twstat));

    public Task<Rremove> RemoveAsync(Tremove tremove) 
        => _runtime != null ? _runtime.RemoveAsync(_basePath, tremove, Dialect) : _fs!.RemoveAsync(tremove);

    public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
        => _runtime != null ? _runtime.RemoveAsync(relativePath, tremove, dialect) : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.RemoveAsync(tremove));

    public Task<Rcreate> CreateAsync(Tcreate tcreate) 
        => _runtime != null ? _runtime.CreateAsync(_basePath, tcreate, Dialect) : _fs!.CreateAsync(tcreate);

    public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
        => _runtime != null ? _runtime.CreateAsync(parentRelativePath, tcreate, dialect) : WithPreparedFileSystemAsync(parentRelativePath, dialect, fs => fs.CreateAsync(tcreate));

    public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_runtime is IReaddirCapableBackendRuntime runtimeReaddir)
        {
            return runtimeReaddir.ReaddirAsync(relativePath, treaddir, dialect, ct);
        }

        if (_fs is IReaddirCapableFileSystem fsReaddir)
        {
            return WithPreparedFileSystemAsync(relativePath, dialect, fs => ((IReaddirCapableFileSystem)fs).ReaddirAsync(treaddir));
        }

        return ReaddirViaReadFallbackAsync(relativePath, treaddir, dialect, ct);
    }

    public INinePFileSystem Clone() 
        => _runtime != null ? new RuntimeFileSystemAdapter(_runtime, _basePath) { Dialect = Dialect } : new RuntimeFileSystemAdapter(_fs!.Clone(), _basePath) { Dialect = Dialect };

    public static IBackendRuntime ToRuntime(INinePFileSystem fs) => new RuntimeFileSystemAdapter(fs);

    private async Task<Rreaddir> ReaddirViaReadFallbackAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var read = await (_runtime != null
            ? _runtime.ReadAsync(relativePath, new Tread(treaddir.Tag, treaddir.Fid, treaddir.Offset, treaddir.Count), dialect, ct)
            : WithPreparedFileSystemAsync(relativePath, dialect, fs => fs.ReadAsync(new Tread(treaddir.Tag, treaddir.Fid, treaddir.Offset, treaddir.Count))));

        return new Rreaddir((uint)(NinePConstants.HeaderSize + 4 + read.Data.Length), treaddir.Tag, read.Count, read.Data);
    }

    private Task<Rwalk> WalkOnFreshFileSystemAsync(string[] relativePath, NinePDialect dialect)
        => WithPreparedFileSystemAsync(Array.Empty<string>(), dialect, async fs =>
        {
            if (relativePath.Length == 0)
            {
                return new Rwalk(0, Array.Empty<Qid>());
            }

            return await fs.WalkAsync(new Twalk(0, 0, 0, relativePath));
        });

    private async Task<T> WithPreparedFileSystemAsync<T>(string[] relativePath, NinePDialect dialect, Func<INinePFileSystem, Task<T>> action)
    {
        var fs = _fs!.Clone();
        fs.Dialect = dialect;

        if (relativePath.Length > 0)
        {
            var walk = await fs.WalkAsync(new Twalk(0, 0, 0, relativePath));
            if (walk.Wqid == null || walk.Wqid.Length != relativePath.Length)
            {
                throw new NinePProtocolException("Resolved backend path no longer exists");
            }
        }

        return await action(fs);
    }

    private static string[] Concat(string[] a, string[] b)
    {
        if (a == null || a.Length == 0) return b ?? Array.Empty<string>();
        if (b == null || b.Length == 0) return a;
        var result = new string[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}

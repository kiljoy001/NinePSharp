using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public abstract class LocalNodeBase : INinePNode
{
    protected readonly FileSystemInfo _info;
    public string Name => _info.Name;
    public IUser User { get; protected set; } = IdentityProvider.GetUser("root");
    public IGroup Group { get; protected set; } = IdentityProvider.GetGroup("root");

    protected LocalNodeBase(FileSystemInfo info)
    {
        _info = info;
    }

    public virtual Stat GetStat(NinePDialect dialect)
    {
        var qidType = (_info.Attributes & FileAttributes.Directory) != 0 ? QidType.QTDIR : QidType.QTFILE;
        var qid = new Qid(qidType, 0, (ulong)_info.FullName.GetHashCode());
        uint mode = (uint)((_info.Attributes & FileAttributes.Directory) != 0 ? NinePConstants.FileMode9P.DMDIR : 0) | 0644;

        long length = 0;
        if (_info is FileInfo fi) length = fi.Length;

        return new Stat(0, 0, 0, qid, mode,
            (uint)new DateTimeOffset(_info.LastAccessTimeUtc).ToUnixTimeSeconds(),
            (uint)new DateTimeOffset(_info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            (ulong)length, Name, User.Name, Group.Name, User.Name, dialect);
    }

    public abstract Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct);
    public abstract Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct);
    public abstract Task<INinePNode?> WalkAsync(string name, CancellationToken ct);
    public abstract Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct);
    public abstract Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct);
    public abstract Task RemoveAsync(string name, CancellationToken ct);

    public virtual Task SymlinkAsync(string name, string target, CancellationToken ct) => throw new NotSupportedException();
    public virtual Task<string> ReadlinkAsync(CancellationToken ct) => throw new NotSupportedException();
    public virtual Task LinkAsync(string name, INinePNode target, CancellationToken ct) => throw new NotSupportedException();

    public virtual Task<Rlerror> LockAsync(Tlock msg, CancellationToken ct) => throw new NotSupportedException();
    public virtual Task<Rgetlock> GetlockAsync(Tgetlock msg, CancellationToken ct) => throw new NotSupportedException();
    public virtual Task<Rxattrwalk> XattrwalkAsync(Txattrwalk msg, CancellationToken ct) => throw new NotSupportedException();
    public virtual Task<Rxattrcreate> XattrcreateAsync(Txattrcreate msg, CancellationToken ct) => throw new NotSupportedException();

    public virtual Task WstatAsync(Stat stat, CancellationToken ct)
    {
        if (stat.Name != null && stat.Name.Length > 0 && stat.Name != Name)
        {
            var newPath = Path.Combine(Path.GetDirectoryName(_info.FullName)!, stat.Name);
            if (_info is FileInfo fi) fi.MoveTo(newPath);
            else if (_info is DirectoryInfo di) di.MoveTo(newPath);
        }
        return Task.CompletedTask;
    }
}

public class LocalFile : LocalNodeBase
{
    public LocalFile(FileInfo info) : base(info) { }

    public override async Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
    {
        using var fs = new FileStream(_info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek((long)offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        int read = await fs.ReadAsync(buffer, 0, (int)count, ct);
        if (read < count) Array.Resize(ref buffer, read);
        return buffer;
    }

    public override async Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
    {
        using var fs = new FileStream(_info.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        fs.Seek((long)offset, SeekOrigin.Begin);
        await fs.WriteAsync(data, 0, data.Length, ct);
        return (uint)data.Length;
    }

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct) => throw new NotSupportedException();
    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct) => throw new NotSupportedException();
    public override Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct) => throw new NotSupportedException();
    public override Task RemoveAsync(string name, CancellationToken ct) => throw new NotSupportedException();
}

public class LocalDir : LocalNodeBase
{
    public LocalDir(DirectoryInfo info) : base(info) { }

    public override Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct) => throw new NotSupportedException();
    public override Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct) => throw new NotSupportedException();

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct)
    {
        if (name == "..") return Task.FromResult<INinePNode?>(new LocalDir(((DirectoryInfo)_info).Parent ?? (DirectoryInfo)_info));

        var fullPath = Path.Combine(_info.FullName, name);
        if (File.Exists(fullPath)) return Task.FromResult<INinePNode?>(new LocalFile(new FileInfo(fullPath)));
        if (Directory.Exists(fullPath)) return Task.FromResult<INinePNode?>(new LocalDir(new DirectoryInfo(fullPath)));

        return Task.FromResult<INinePNode?>(null);
    }

    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct)
    {
        var di = (DirectoryInfo)_info;
        var children = new List<INinePNode>();
        foreach (var d in di.GetDirectories()) children.Add(new LocalDir(d));
        foreach (var f in di.GetFiles()) children.Add(new LocalFile(f));
        return Task.FromResult<IEnumerable<INinePNode>>(children);
    }

    public override Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct)
    {
        var fullPath = Path.Combine(_info.FullName, name);
        if ((perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            return Task.FromResult<INinePNode>(new LocalDir(Directory.CreateDirectory(fullPath)));
        }
        else
        {
            var fi = new FileInfo(fullPath);
            using (fi.Create()) { }
            return Task.FromResult<INinePNode>(new LocalFile(fi));
        }
    }

    public override Task RemoveAsync(string name, CancellationToken ct)
    {
        var fullPath = Path.Combine(_info.FullName, name);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        else if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
        return Task.CompletedTask;
    }
}

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
    protected LocalNodeBase(FileSystemInfo info)
    {
        Info = info;
    }

    public string Name => Info.Name;

    public IUser User { get; protected set; } = IdentityProvider.GetUser("root");

    public IGroup Group { get; protected set; } = IdentityProvider.GetGroup("root");

    protected FileSystemInfo Info { get; }

    public virtual Stat GetStat(NinePDialect dialect)
    {
        var isDirectory = (Info.Attributes & FileAttributes.Directory) != 0;
        var qidType = isDirectory ? QidType.QTDIR : QidType.QTFILE;
        var qid = new Qid(qidType, 0, (ulong)Info.FullName.GetHashCode());
        uint mode = isDirectory
            ? (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755
            : NinePConstants.Mode0644;

        long length = 0;
        if (Info is FileInfo fi)
        {
            length = fi.Length;
        }

        return new Stat(
            0,
            0,
            0,
            qid,
            mode,
            (uint)new DateTimeOffset(Info.LastAccessTimeUtc).ToUnixTimeSeconds(),
            (uint)new DateTimeOffset(Info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            (ulong)length,
            Name,
            User.Name,
            Group.Name,
            User.Name,
            dialect);
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
            var newPath = Path.Combine(Path.GetDirectoryName(Info.FullName)!, stat.Name);
            if (Info is FileInfo fi)
            {
                fi.MoveTo(newPath);
            }
            else if (Info is DirectoryInfo di)
            {
                di.MoveTo(newPath);
            }
        }

        return Task.CompletedTask;
    }
}

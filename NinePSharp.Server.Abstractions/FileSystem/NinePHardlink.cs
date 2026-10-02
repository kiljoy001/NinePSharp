using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public class NinePHardlink : INinePNode
{
    private readonly INinePNode inner;

    public NinePHardlink(string name, INinePNode inner)
    {
        Name = name;
        this.inner = inner;
    }

    public string Name { get; }

    public IUser User => inner.User;

    public IGroup Group => inner.Group;

    public Stat GetStat(NinePDialect dialect)
    {
        var stat = inner.GetStat(dialect);
        return new Stat(stat.Size, stat.Type, stat.Dev, stat.Qid, stat.Mode, stat.Atime, stat.Mtime, stat.Length, Name, stat.Uid, stat.Gid, stat.Muid, dialect);
    }

    public Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct) => inner.ReadAsync(offset, count, ct);

    public Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct) => inner.WriteAsync(offset, data, ct);

    public Task<INinePNode?> WalkAsync(string name, CancellationToken ct) => inner.WalkAsync(name, ct);

    public Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct) => inner.ReaddirAsync(ct);

    public Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct) => inner.CreateAsync(name, perm, mode, ct);

    public Task RemoveAsync(string name, CancellationToken ct) => inner.RemoveAsync(name, ct);

    public Task WstatAsync(Stat stat, CancellationToken ct) => inner.WstatAsync(stat, ct);

    public Task SymlinkAsync(string name, string target, CancellationToken ct) => inner.SymlinkAsync(name, target, ct);

    public Task<string> ReadlinkAsync(CancellationToken ct) => inner.ReadlinkAsync(ct);

    public Task LinkAsync(string name, INinePNode target, CancellationToken ct) => inner.LinkAsync(name, target, ct);

    public Task<Rlerror> LockAsync(Tlock msg, CancellationToken ct) => inner.LockAsync(msg, ct);

    public Task<Rgetlock> GetlockAsync(Tgetlock msg, CancellationToken ct) => inner.GetlockAsync(msg, ct);

    public Task<Rxattrwalk> XattrwalkAsync(Txattrwalk msg, CancellationToken ct) => inner.XattrwalkAsync(msg, ct);

    public Task<Rxattrcreate> XattrcreateAsync(Txattrcreate msg, CancellationToken ct) => inner.XattrcreateAsync(msg, ct);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public abstract class NinePNodeBase : INinePNode
{
    private static long nextPath = 1;

    protected NinePNodeBase(string name, QidType type)
    {
        Name = name;
        var path = (ulong)Interlocked.Increment(ref nextPath);
        Qid = new Qid(type, 0, path);
        Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Mtime = Atime;
    }

    public string Name { get; set; }

    public Qid Qid { get; protected set; }

    public uint Mode { get; set; }

    public uint Atime { get; set; }

    public uint Mtime { get; set; }

    public ulong Length { get; set; }

    public IUser User { get; set; } = IdentityProvider.GetUser("root");

    public IGroup Group { get; set; } = IdentityProvider.GetGroup("root");

    public string Muid => User.Name;

    public virtual Stat GetStat(NinePDialect dialect)
    {
        return new Stat(0, 0, 0, Qid, Mode, Atime, Mtime, Length, Name, User.Name, Group.Name, Muid, dialect);
    }

    public virtual Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
        => throw new NotSupportedException("Read not supported on this node.");

    public virtual Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
        => throw new NotSupportedException("Write not supported on this node.");

    public virtual Task<INinePNode?> WalkAsync(string name, CancellationToken ct)
        => throw new NotSupportedException("Walk not supported on this node.");

    public virtual Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct)
        => throw new NotSupportedException("Readdir not supported on this node.");

    public virtual Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct)
        => throw new NotSupportedException("Create not supported on this node.");

    public virtual Task RemoveAsync(string name, CancellationToken ct)
        => throw new NotSupportedException("Remove not supported on this node.");

    public virtual Task WstatAsync(Stat stat, CancellationToken ct)
    {
        if (stat.Name != null && stat.Name.Length > 0)
        {
            Name = stat.Name;
        }

        if (stat.Mode != uint.MaxValue)
        {
            Mode = stat.Mode;
        }

        if (stat.Atime != uint.MaxValue)
        {
            Atime = stat.Atime;
        }

        if (stat.Mtime != uint.MaxValue)
        {
            Mtime = stat.Mtime;
        }

        if (stat.Length != ulong.MaxValue)
        {
            Length = stat.Length;
        }

        // User/Group updates could be added here if needed
        return Task.CompletedTask;
    }

    public virtual Task SymlinkAsync(string name, string target, CancellationToken ct)
        => throw new NotSupportedException("Symlink not supported on this node.");

    public virtual Task<string> ReadlinkAsync(CancellationToken ct)
        => throw new NotSupportedException("Readlink not supported on this node.");

    public virtual Task LinkAsync(string name, INinePNode target, CancellationToken ct)
        => throw new NotSupportedException("Link not supported on this node.");

    public virtual Task<Rlerror> LockAsync(Tlock msg, CancellationToken ct)
        => throw new NotSupportedException("Lock not supported on this node.");

    public virtual Task<Rgetlock> GetlockAsync(Tgetlock msg, CancellationToken ct)
        => throw new NotSupportedException("Getlock not supported on this node.");

    public virtual Task<Rxattrwalk> XattrwalkAsync(Txattrwalk msg, CancellationToken ct)
        => throw new NotSupportedException("Xattrwalk not supported on this node.");

    public virtual Task<Rxattrcreate> XattrcreateAsync(Txattrcreate msg, CancellationToken ct)
        => throw new NotSupportedException("Xattrcreate not supported on this node.");
}

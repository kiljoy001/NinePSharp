using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public abstract class NinePNodeBase : INinePNode
{
    private static long _nextPath = 1;

    public string Name { get; set; }
    public Qid Qid { get; protected set; }
    public uint Mode { get; set; }
    public uint Atime { get; set; }
    public uint Mtime { get; set; }
    public ulong Length { get; set; }
    public IUser User { get; set; } = IdentityProvider.GetUser("root");
    public IGroup Group { get; set; } = IdentityProvider.GetGroup("root");
    public string Muid => User.Name;

    protected NinePNodeBase(string name, QidType type)
    {
        Name = name;
        var path = (ulong)Interlocked.Increment(ref _nextPath);
        Qid = new Qid(type, 0, path);
        Atime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Mtime = Atime;
    }

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
        if (stat.Name != null && stat.Name.Length > 0) Name = stat.Name;
        if (stat.Mode != uint.MaxValue) Mode = stat.Mode;
        if (stat.Atime != uint.MaxValue) Atime = stat.Atime;
        if (stat.Mtime != uint.MaxValue) Mtime = stat.Mtime;
        if (stat.Length != ulong.MaxValue) Length = stat.Length;
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

public class NinePSymlink : NinePNodeBase
{
    private string _target;
    public NinePSymlink(string name, string target) : base(name, QidType.QTSYMLINK)
    {
        _target = target;
        Mode = NinePConstants.Mode0777;
    }

    public override Task<string> ReadlinkAsync(CancellationToken ct) => Task.FromResult(_target);
}

public class NinePFile : NinePNodeBase
{
    private byte[] _content = Array.Empty<byte>();

    public NinePFile(string name) : base(name, QidType.QTFILE)
    {
        Mode = NinePConstants.Mode0644;
    }

    public NinePFile(string name, byte[] content) : this(name)
    {
        _content = content;
        Length = (ulong)content.Length;
    }

    public override Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
    {
        if (offset >= (ulong)_content.Length) return Task.FromResult(Array.Empty<byte>());
        int available = _content.Length - (int)offset;
        int toRead = Math.Min((int)count, available);
        byte[] result = new byte[toRead];
        Array.Copy(_content, (int)offset, result, 0, toRead);
        return Task.FromResult(result);
    }

    public override Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
    {
        if (offset == 0)
        {
            _content = data;
        }
        else
        {
            if (offset + (ulong)data.Length > (ulong)_content.Length)
            {
                Array.Resize(ref _content, (int)offset + data.Length);
            }
            Array.Copy(data, 0, _content, (int)offset, data.Length);
        }
        Length = (ulong)_content.Length;
        Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult((uint)data.Length);
    }
}

public class NinePDir : NinePNodeBase
{
    private readonly List<INinePNode> _children = new();

    public NinePDir(string name) : base(name, QidType.QTDIR)
    {
        Mode = (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755;
    }

    public void AddChild(INinePNode node) => _children.Add(node);

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct)
    {
        if (name == "..") return Task.FromResult<INinePNode?>(this);
        return Task.FromResult(_children.Find(c => c.Name == name));
    }

    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct)
    {
        return Task.FromResult<IEnumerable<INinePNode>>(_children);
    }

    public override Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct)
    {
        INinePNode newNode;
        if ((perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            newNode = new NinePDir(name) { Mode = perm };
        }
        else
        {
            newNode = new NinePFile(name) { Mode = perm };
        }
        _children.Add(newNode);
        return Task.FromResult(newNode);
    }

    public override Task RemoveAsync(string name, CancellationToken ct)
    {
        _children.RemoveAll(c => c.Name == name);
        return Task.CompletedTask;
    }

    public override Task SymlinkAsync(string name, string target, CancellationToken ct)
    {
        var link = new NinePSymlink(name, target);
        _children.Add(link);
        return Task.CompletedTask;
    }

    public override Task LinkAsync(string name, INinePNode target, CancellationToken ct)
    {
        // For in-memory hardlinks, we just add the same node object with a new name
        // but wait, INinePNode has a Name property. We need a wrapper for name alias.
        var alias = new NinePHardlink(name, target);
        _children.Add(alias);
        return Task.CompletedTask;
    }
}

public class NinePHardlink : INinePNode
{
    private readonly INinePNode _inner;
    public string Name { get; }
    public IUser User => _inner.User;
    public IGroup Group => _inner.Group;

    public NinePHardlink(string name, INinePNode inner)
    {
        Name = name;
        _inner = inner;
    }

    public Stat GetStat(NinePDialect dialect)
    {
        var stat = _inner.GetStat(dialect);
        return new Stat(stat.Size, stat.Type, stat.Dev, stat.Qid, stat.Mode, stat.Atime, stat.Mtime, stat.Length, Name, stat.Uid, stat.Gid, stat.Muid, dialect);
    }

    public Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct) => _inner.ReadAsync(offset, count, ct);
    public Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct) => _inner.WriteAsync(offset, data, ct);
    public Task<INinePNode?> WalkAsync(string name, CancellationToken ct) => _inner.WalkAsync(name, ct);
    public Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct) => _inner.ReaddirAsync(ct);
    public Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct) => _inner.CreateAsync(name, perm, mode, ct);
    public Task RemoveAsync(string name, CancellationToken ct) => _inner.RemoveAsync(name, ct);
    public Task WstatAsync(Stat stat, CancellationToken ct) => _inner.WstatAsync(stat, ct);
    public Task SymlinkAsync(string name, string target, CancellationToken ct) => _inner.SymlinkAsync(name, target, ct);
    public Task<string> ReadlinkAsync(CancellationToken ct) => _inner.ReadlinkAsync(ct);
    public Task LinkAsync(string name, INinePNode target, CancellationToken ct) => _inner.LinkAsync(name, target, ct);
    public Task<Rlerror> LockAsync(Tlock msg, CancellationToken ct) => _inner.LockAsync(msg, ct);
    public Task<Rgetlock> GetlockAsync(Tgetlock msg, CancellationToken ct) => _inner.GetlockAsync(msg, ct);
    public Task<Rxattrwalk> XattrwalkAsync(Txattrwalk msg, CancellationToken ct) => _inner.XattrwalkAsync(msg, ct);
    public Task<Rxattrcreate> XattrcreateAsync(Txattrcreate msg, CancellationToken ct) => _inner.XattrcreateAsync(msg, ct);
}

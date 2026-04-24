using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public interface INinePNode
{
    string Name { get; }
    IUser User { get; }
    IGroup Group { get; }
    Stat GetStat(NinePDialect dialect);
    
    Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct);
    Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct);
    
    // For directories
    Task<INinePNode?> WalkAsync(string name, CancellationToken ct);
    Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct);
    
    Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct);
    Task RemoveAsync(string name, CancellationToken ct);
    Task WstatAsync(Stat stat, CancellationToken ct);
    
    Task SymlinkAsync(string name, string target, CancellationToken ct);
    Task<string> ReadlinkAsync(CancellationToken ct);
    Task LinkAsync(string name, INinePNode target, CancellationToken ct);

    // 9P2000.L Locking
    Task<Rlerror> LockAsync(Tlock msg, CancellationToken ct);
    Task<Rgetlock> GetlockAsync(Tgetlock msg, CancellationToken ct);

    // 9P2000.L Xattr
    Task<Rxattrwalk> XattrwalkAsync(Txattrwalk msg, CancellationToken ct);
    Task<Rxattrcreate> XattrcreateAsync(Txattrcreate msg, CancellationToken ct);
}

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

public class LocalDir : LocalNodeBase
{
    public LocalDir(DirectoryInfo info)
        : base(info)
    {
    }

    public override Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct) => throw new NotSupportedException();

    public override Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct) => throw new NotSupportedException();

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct)
    {
        if (name == "..")
        {
            return Task.FromResult<INinePNode?>(new LocalDir(((DirectoryInfo)Info).Parent ?? (DirectoryInfo)Info));
        }

        var fullPath = Path.Combine(Info.FullName, name);
        if (File.Exists(fullPath))
        {
            return Task.FromResult<INinePNode?>(new LocalFile(new FileInfo(fullPath)));
        }

        if (Directory.Exists(fullPath))
        {
            return Task.FromResult<INinePNode?>(new LocalDir(new DirectoryInfo(fullPath)));
        }

        return Task.FromResult<INinePNode?>(null);
    }

    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct)
    {
        var di = (DirectoryInfo)Info;
        var children = new List<INinePNode>();
        foreach (var d in di.GetDirectories())
        {
            children.Add(new LocalDir(d));
        }

        foreach (var f in di.GetFiles())
        {
            children.Add(new LocalFile(f));
        }

        return Task.FromResult<IEnumerable<INinePNode>>(children);
    }

    public override Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct)
    {
        var fullPath = Path.Combine(Info.FullName, name);
        if ((perm & (uint)NinePConstants.FileMode9P.DMDIR) != 0)
        {
            return Task.FromResult<INinePNode>(new LocalDir(Directory.CreateDirectory(fullPath)));
        }
        else
        {
            var fi = new FileInfo(fullPath);
            using (fi.Create())
            {
            }

            return Task.FromResult<INinePNode>(new LocalFile(fi));
        }
    }

    public override Task RemoveAsync(string name, CancellationToken ct)
    {
        var fullPath = Path.Combine(Info.FullName, name);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }

        return Task.CompletedTask;
    }
}

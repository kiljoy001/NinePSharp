using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public class NinePDir : NinePNodeBase
{
    private readonly List<INinePNode> children = new();

    public NinePDir(string name)
        : base(name, QidType.QTDIR)
    {
        Mode = (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755;
    }

    public void AddChild(INinePNode node) => children.Add(node);

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct)
    {
        if (name == "..")
        {
            return Task.FromResult<INinePNode?>(this);
        }

        return Task.FromResult(children.Find(c => c.Name == name));
    }

    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct)
    {
        return Task.FromResult<IEnumerable<INinePNode>>(children);
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

        children.Add(newNode);
        return Task.FromResult(newNode);
    }

    public override Task RemoveAsync(string name, CancellationToken ct)
    {
        children.RemoveAll(c => c.Name == name);
        return Task.CompletedTask;
    }

    public override Task SymlinkAsync(string name, string target, CancellationToken ct)
    {
        var link = new NinePSymlink(name, target);
        children.Add(link);
        return Task.CompletedTask;
    }

    public override Task LinkAsync(string name, INinePNode target, CancellationToken ct)
    {
        // For in-memory hardlinks, we just add the same node object with a new name
        // but wait, INinePNode has a Name property. We need a wrapper for name alias.
        var alias = new NinePHardlink(name, target);
        children.Add(alias);
        return Task.CompletedTask;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public class NinePSymlink : NinePNodeBase
{
    private string target;

    public NinePSymlink(string name, string target)
        : base(name, QidType.QTSYMLINK)
    {
        this.target = target;
        Mode = NinePConstants.Mode0777;
    }

    public override Task<string> ReadlinkAsync(CancellationToken ct) => Task.FromResult(target);
}

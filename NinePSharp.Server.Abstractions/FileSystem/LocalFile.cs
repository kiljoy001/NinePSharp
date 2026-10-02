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

public class LocalFile : LocalNodeBase
{
    public LocalFile(FileInfo info)
        : base(info)
    {
    }

    public override async Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
    {
        using var fs = new FileStream(Info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek((long)offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        int read = await fs.ReadAsync(buffer, 0, (int)count, ct);
        if (read < count)
        {
            Array.Resize(ref buffer, read);
        }

        return buffer;
    }

    public override async Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
    {
        using var fs = new FileStream(Info.FullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        fs.Seek((long)offset, SeekOrigin.Begin);
        await fs.WriteAsync(data, 0, data.Length, ct);
        return (uint)data.Length;
    }

    public override Task<INinePNode?> WalkAsync(string name, CancellationToken ct) => throw new NotSupportedException();

    public override Task<IEnumerable<INinePNode>> ReaddirAsync(CancellationToken ct) => throw new NotSupportedException();

    public override Task<INinePNode> CreateAsync(string name, uint perm, byte mode, CancellationToken ct) => throw new NotSupportedException();

    public override Task RemoveAsync(string name, CancellationToken ct) => throw new NotSupportedException();
}

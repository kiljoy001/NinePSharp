using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests.Helpers;

internal sealed class DirectoryListingFileSystem : TestHandlerBase
{
    private readonly string[] entries;

    internal DirectoryListingFileSystem(IEnumerable<string> entries)
    {
        this.entries = entries.ToArray();
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var qids = twalk.Wname.Select((name, i) => new Qid(QidType.QTDIR, 0, (ulong)Math.Abs((name + i).GetHashCode()))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        var allStats = new List<byte>();
        foreach (var name in entries)
        {
            var qid = new Qid(QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode()));
            var stat = new Stat(0, 0, 0, qid, (uint)NinePConstants.FileMode9P.DMDIR | NinePConstants.Mode0755, 0, 0, 0, name, "none", "none", "none", NinePDialect.NineP2000);
            var buffer = new byte[stat.Size];
            int off = 0;
            stat.WriteTo(buffer, ref off);
            allStats.AddRange(buffer);
        }

        if (tread.Offset >= (ulong)allStats.Count)
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        int start = (int)tread.Offset;
        int len = (int)Math.Min(tread.Count, (uint)(allStats.Count - start));
        return Task.FromResult(new Rread(tread.Tag, allStats.GetRange(start, len).ToArray()));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0755, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, NinePDialect.NineP2000)));
}

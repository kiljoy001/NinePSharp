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

internal sealed class CreateTrackingFileSystem : TestHandlerBase
{
    private readonly string marker;
    private readonly List<string> created = new();

    internal CreateTrackingFileSystem(string marker)
    {
        this.marker = marker;
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var qids = twalk.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, (ulong)marker.GetHashCode()), 0));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        var payload = created.Count == 0 ? marker : string.Join(",", created);
        return Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(payload)));
    }

    public override Task<Rcreate> CreateAsync(string[] relativePath, Tcreate tcreate, CancellationToken ct)
    {
        created.Add(tcreate.Name);
        ulong path = (ulong)Math.Abs((marker + ":" + tcreate.Name).GetHashCode());
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, path), 8192));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0755, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, NinePDialect.NineP2000)));
}

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

internal sealed class MarkerFileSystem : TestHandlerBase
{
    private readonly string marker;

    internal MarkerFileSystem(string marker)
    {
        this.marker = marker;
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk msg, CancellationToken ct)
    {
        var qids = msg.Wname.Select((_, i) => new Qid(QidType.QTFILE, 0, (ulong)(marker.GetHashCode() + i + 1))).ToArray();
        return Task.FromResult(new Rwalk(msg.Tag, qids));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen msg, CancellationToken ct)
    {
        return Task.FromResult(new Ropen(msg.Tag, new Qid(QidType.QTFILE, 0, (ulong)marker.GetHashCode()), 0));
    }

    public override Task<Rread> ReadAsync(string[] relativePath, Tread msg, CancellationToken ct)
    {
        return Task.FromResult(new Rread(msg.Tag, Encoding.UTF8.GetBytes(marker)));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite msg, CancellationToken ct) => Task.FromException<Rwrite>(new Exception("Not supported"));

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat msg, CancellationToken ct) => Task.FromResult(new Rstat(msg.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0755, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, NinePDialect.NineP2000)));
}

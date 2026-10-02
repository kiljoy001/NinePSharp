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

internal sealed class ExistingPathFileSystem : TestHandlerBase
{
    private readonly HashSet<string> paths;

    internal ExistingPathFileSystem(IEnumerable<string> paths)
    {
        this.paths = new HashSet<string>(paths.Select(NormalizePath), StringComparer.Ordinal) { "/" };
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var temp = new List<string>(relativePath);
        var qids = new List<Qid>();

        foreach (var segment in twalk.Wname)
        {
            if (segment == "..")
            {
                if (temp.Count > 0)
                {
                    temp.RemoveAt(temp.Count - 1);
                }
            }
            else if (segment != ".")
            {
                temp.Add(segment);
            }

            string path = Normalize(temp);
            if (!paths.Contains(path))
            {
                return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
            }

            qids.Add(new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(path.GetHashCode())));
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 8192));

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
        => Task.FromResult(new Rread(tread.Tag, Encoding.UTF8.GetBytes(Normalize(relativePath))));

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0755, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, NinePDialect.NineP2000)));

    private static string Normalize(IEnumerable<string> segments)
    {
        var list = segments.ToList();
        return list.Count == 0 ? "/" : "/" + string.Join("/", list);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return Normalize(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}

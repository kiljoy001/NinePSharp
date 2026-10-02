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

internal sealed class SharedMutableFileSystem : TestHandlerBase
{
    private readonly SharedState state;

    internal SharedMutableFileSystem()
    {
        state = new SharedState();
    }

    public override Task<Rwalk> WalkAsync(string[] relativePath, Twalk twalk, CancellationToken ct)
    {
        var tempPath = new List<string>(relativePath);
        var qids = new List<Qid>();

        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0)
                {
                    tempPath.RemoveAt(tempPath.Count - 1);
                }
            }
            else if (name != ".")
            {
                tempPath.Add(name);
            }

            string path = tempPath.Count == 0 ? "/" : "/" + string.Join("/", tempPath);
            var qidType = state.Files.ContainsKey(path) ? QidType.QTFILE : QidType.QTDIR;
            qids.Add(new Qid(qidType, 0, (ulong)Math.Abs(path.GetHashCode())));
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    public override Task<Ropen> OpenAsync(string[] relativePath, Topen topen, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        var qidType = state.Files.ContainsKey(path) ? QidType.QTFILE : QidType.QTDIR;
        return Task.FromResult(new Ropen(topen.Tag, new Qid(qidType, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public override Task<Rread> ReadAsync(string[] relativePath, Tread tread, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        if (!state.Files.TryGetValue(path, out var data))
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        if (tread.Offset >= (ulong)data.Length)
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        int offset = (int)tread.Offset;
        int count = Math.Min((int)tread.Count, data.Length - offset);
        return Task.FromResult(new Rread(tread.Tag, data.AsSpan(offset, count).ToArray()));
    }

    public override Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, CancellationToken ct)
    {
        string path = GetFullPath(relativePath);
        if (!state.Files.TryGetValue(path, out var existing))
        {
            existing = Array.Empty<byte>();
        }

        int offset = (int)twrite.Offset;
        byte[] incoming = twrite.Data.ToArray();
        byte[] content = new byte[Math.Max(existing.Length, offset + incoming.Length)];
        existing.CopyTo(content, 0);
        incoming.CopyTo(content, offset);
        state.Files[path] = content;
        return Task.FromResult(new Rwrite(twrite.Tag, (uint)incoming.Length));
    }

    public override Task<Rcreate> CreateAsync(string[] relativePath, Tcreate tcreate, CancellationToken ct)
    {
        string parent = GetFullPath(relativePath);
        string path = parent == "/" ? "/" + tcreate.Name : parent + "/" + tcreate.Name;
        state.Files[path] = Array.Empty<byte>();
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(path.GetHashCode())), 8192));
    }

    public override Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, CancellationToken ct) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 0), NinePConstants.Mode0755, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, NinePDialect.NineP2000)));

    private string GetFullPath(string[] rel) => rel.Length == 0 ? "/" : "/" + string.Join("/", rel);

    private sealed class SharedState
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal)
        {
            ["/"] = Array.Empty<byte>(),
        };
    }
}

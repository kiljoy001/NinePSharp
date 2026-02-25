using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Constants;
using NinePSharp.Protocol;
using NinePSharp.Server.Utils;

namespace NinePSharp.Backends.Pipes;

public class PipeFileSystem : INinePFileSystem
{
    private List<string> _currentPath = new();
    private static readonly ConcurrentDictionary<string, IPipeNode> _queues = new();
    private static readonly ConcurrentDictionary<string, IPipeNode> _pipes = new();

    public bool DotU { get; set; }

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                if (tempPath.Count == 0)
                {
                    if (name != "queues" && name != "pipes") break;
                }
                else if (tempPath.Count == 1)
                {
                    // Check if node exists
                    var map = tempPath[0] == "queues" ? _queues : _pipes;
                    if (!map.ContainsKey(name)) { /* mkdir needed */ }
                }
                tempPath.Add(name);
            }
            qids.Add(GetQidForPath(tempPath));
        }

        if (qids.Count == twalk.Wname.Length) _currentPath = tempPath;
        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    private Qid GetQidForPath(List<string> path)
    {
        bool isDir = path.Count <= 1;
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        return new Qid(type, 0, DeterministicHash.GetStableHash64(string.Join("/", path)));
    }

    public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, GetQidForPath(_currentPath), 0));

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_currentPath.Count == 2)
        {
            var map = _currentPath[0] == "queues" ? _queues : _pipes;
            if (map.TryGetValue(_currentPath[1], out var node))
            {
                var data = await node.ReadAsync(tread.Count);
                return new Rread(tread.Tag, data.ToArray());
            }
        }
        else if (_currentPath.Count < 2)
        {
            // Directory listing logic...
            return new Rread(tread.Tag, Array.Empty<byte>()); 
        }
        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 2)
        {
            var map = _currentPath[0] == "queues" ? _queues : _pipes;
            if (map.TryGetValue(_currentPath[1], out var node))
            {
                await node.WriteAsync(twrite.Data.ToArray());
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "ipc";
        var isDir = _currentPath.Count <= 1;
        var stat = new Stat(0, 0, 0, GetQidForPath(_currentPath), 
            0666 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();

    public Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count >= 2)
        {
            var category = _currentPath[0];
            var name = _currentPath[1];
            var map = category == "queues" ? _queues : _pipes;
            
            if (map.TryRemove(name, out var node))
            {
                node.Close();
                return Task.FromResult(new Rremove(tremove.Tag));
            }
        }
        throw new NinePNotSupportedException($"Cannot remove path: {string.Join("/", _currentPath)}");
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr) => Task.FromResult(new Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, GetQidForPath(_currentPath), 0666));
    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public Task<Rmkdir> MkdirAsync(Tmkdir tmkdir)
    {
        if (_currentPath.Count == 1)
        {
            IPipeNode node = _currentPath[0] == "queues" 
                ? new ObjectQueueNode(tmkdir.Name) 
                : new DataPipeNode(tmkdir.Name);
            
            var map = _currentPath[0] == "queues" ? _queues : _pipes;
            if (map.TryAdd(tmkdir.Name, node))
            {
                return Task.FromResult(new Rmkdir(0, tmkdir.Tag, GetQidForPath(new List<string>(_currentPath) { tmkdir.Name })));
            }
        }
        throw new InvalidOperationException("Invalid path for creation");
    }

    public INinePFileSystem Clone()
    {
        var clone = new PipeFileSystem();
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

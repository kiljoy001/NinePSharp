using System;
using System.Buffers.Binary;
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

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);
        bool failed = false;

        foreach (var name in twalk.Wname)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                failed = true;
                break;
            }

            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                if (tempPath.Count == 0)
                {
                    if (name != "queues" && name != "pipes")
                    {
                        failed = true;
                        break;
                    }
                }
                else if (tempPath.Count == 1)
                {
                    if (!TryGetCategoryMap(tempPath[0], out var map) || !map.ContainsKey(name))
                    {
                        failed = true;
                        break;
                    }
                }
                else
                {
                    // Leaf nodes don't have children in this backend.
                    failed = true;
                    break;
                }

                tempPath.Add(name);
            }

            qids.Add(GetQidForPath(tempPath));
        }

        if (!failed && qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    private Qid GetQidForPath(List<string> path)
    {
        bool isDir = path.Count <= 1;
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        return new Qid(type, 0, DeterministicHash.GetStableHash64(string.Join("/", path)));
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        if (_currentPath.Count == 2)
        {
            if (!TryGetNodeAtCurrentPath(out _))
            {
                throw new NinePNotFoundException($"Node not found: {GetCurrentPathText()}");
            }
        }

        return Task.FromResult(new Ropen(topen.Tag, GetQidForPath(_currentPath), 0));
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_currentPath.Count <= 1)
        {
            var payload = BuildDirectoryReadPayload(tread.Offset, tread.Count);
            return new Rread(tread.Tag, payload);
        }

        if (_currentPath.Count == 2)
        {
            if (!TryGetNodeAtCurrentPath(out var node))
            {
                throw new NinePNotFoundException($"Node not found: {GetCurrentPathText()}");
            }

            var data = await node.ReadAsync(tread.Count);
            return new Rread(tread.Tag, data.ToArray());
        }

        throw new NinePNotFoundException($"Invalid path: {GetCurrentPathText()}");
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count != 2)
        {
            throw new NinePInvalidOperationException($"Cannot write to path: {GetCurrentPathText()}");
        }

        if (!TryGetNodeAtCurrentPath(out var node))
        {
            throw new NinePNotFoundException($"Node not found: {GetCurrentPathText()}");
        }

        await node.WriteAsync(twrite.Data.ToArray());
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        if (_currentPath.Count == 2 && !TryGetNodeAtCurrentPath(out _))
        {
            throw new NinePNotFoundException($"Node not found: {GetCurrentPathText()}");
        }

        var name = _currentPath.LastOrDefault() ?? "ipc";
        var isDir = _currentPath.Count <= 1;
        var stat = new Stat(0, 0, 0, GetQidForPath(_currentPath), 
            0666 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();

    public Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count >= 2 && TryGetCategoryMap(_currentPath[0], out var map))
        {
            var name = _currentPath[1];
            
            if (map.TryRemove(name, out var node))
            {
                node.Close();
                _currentPath = _currentPath.Take(1).ToList();
                return Task.FromResult(new Rremove(tremove.Tag));
            }
        }
        throw new NinePNotFoundException($"Cannot remove path: {GetCurrentPathText()}");
    }

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        if (_currentPath.Count == 1 && TryGetCategoryMap(_currentPath[0], out var map))
        {
            IPipeNode node = _currentPath[0] == "queues" 
                ? new ObjectQueueNode(tcreate.Name) 
                : new DataPipeNode(tcreate.Name);
            
            if (map.TryAdd(tcreate.Name, node))
            {
                return Task.FromResult(new Rcreate(tcreate.Tag, GetQidForPath(new List<string>(_currentPath) { tcreate.Name }), 0));
            }

            throw new NinePInvalidOperationException($"Node already exists: {_currentPath[0]}/{tcreate.Name}");
        }

        throw new NinePInvalidOperationException($"Invalid path for creation: {GetCurrentPathText()}");
    }

    public INinePFileSystem Clone()
    {
        var clone = new PipeFileSystem();
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }

    private static bool TryGetCategoryMap(string category, out ConcurrentDictionary<string, IPipeNode> map)
    {
        if (category == "queues")
        {
            map = _queues;
            return true;
        }

        if (category == "pipes")
        {
            map = _pipes;
            return true;
        }

        map = null!;
        return false;
    }

    private bool TryGetNodeAtCurrentPath(out IPipeNode node)
    {
        node = null!;

        if (_currentPath.Count != 2 || !TryGetCategoryMap(_currentPath[0], out var map))
        {
            return false;
        }

        if (!map.TryGetValue(_currentPath[1], out var found) || found is null)
        {
            return false;
        }

        node = found;
        return true;
    }

    private IEnumerable<string> GetDirectoryEntries(List<string> basePath)
    {
        if (basePath.Count == 0)
        {
            return new[] { "pipes", "queues" }.OrderBy(n => n, StringComparer.Ordinal);
        }

        if (basePath.Count == 1 && TryGetCategoryMap(basePath[0], out var map))
        {
            return map.Keys.OrderBy(n => n, StringComparer.Ordinal);
        }

        return Array.Empty<string>();
    }

    private byte[] BuildDirectoryReadPayload(ulong offset, uint count)
    {
        var names = GetDirectoryEntries(_currentPath).ToList();
        var entries = new List<byte>();

        foreach (var name in names)
        {
            var entryPath = new List<string>(_currentPath) { name };
            bool isDir = _currentPath.Count == 0;
            uint mode = isDir ? 0755u | (uint)NinePConstants.FileMode9P.DMDIR : 0666u;
            var stat = new Stat(
                0,
                0,
                0,
                GetQidForPath(entryPath),
                mode,
                0,
                0,
                0,
                name,
                "ipc",
                "ipc",
                "ipc");

            var statBuffer = new byte[stat.Size];
            int statOffset = 0;
            stat.WriteTo(statBuffer, ref statOffset);
            entries.AddRange(statBuffer);
        }

        var allData = entries.ToArray();
        if (offset >= (ulong)allData.Length)
        {
            return Array.Empty<byte>();
        }

        int totalToSend = 0;
        int currentOffset = (int)offset;
        while (currentOffset + 2 <= allData.Length)
        {
            int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2;
            if (entrySize <= 0 || currentOffset + entrySize > allData.Length)
            {
                break;
            }

            if (totalToSend + entrySize > count)
            {
                break;
            }

            totalToSend += entrySize;
            currentOffset += entrySize;
        }

        if (totalToSend == 0)
        {
            return Array.Empty<byte>();
        }

        return allData.AsMemory((int)offset, totalToSend).ToArray();
    }

    private string GetCurrentPathText()
    {
        return _currentPath.Count == 0 ? "/" : "/" + string.Join("/", _currentPath);
    }
}

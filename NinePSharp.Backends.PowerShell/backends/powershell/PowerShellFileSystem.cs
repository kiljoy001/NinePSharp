using NinePSharp.Server.Utils;
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

namespace NinePSharp.Backends.PowerShell;

/// <summary>
/// Implements a 9P grid compute node that allows remote execution of object-oriented PowerShell scripts.
/// </summary>
public class PowerShellFileSystem : INinePFileSystem
{
    private List<string> _currentPath = new();
    private static readonly ConcurrentDictionary<string, PowerShellJob> _globalJobs = new();

    public bool DotU { get; set; }

    public PowerShellFileSystem()
    {
    }

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
                    if (name != "jobs") return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
                }
                else if (tempPath[0] == "jobs")
                {
                    if (tempPath.Count == 1) { /* Allow job ID */ }
                    else if (tempPath.Count == 2)
                    {
                        if (name != "script.ps1" && name != "status" && name != "output.json" && name != "errors")
                            return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
                    }
                    else return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
                }
                tempPath.Add(name);
            }
            qids.Add(GetQidForPath(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return Task.FromResult(new Rwalk(twalk.Tag, qids.ToArray()));
    }

    private Qid GetQidForPath(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        return new Qid(type, 0, DeterministicHash.GetStableHash64(string.Join("/", path)));
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path.Count == 1 && path[0] == "jobs") return true;
        if (path.Count == 2 && path[0] == "jobs") return true;
        return false;
    }

    public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, GetQidForPath(_currentPath), 0));

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[] data = Array.Empty<byte>();

        if (IsDirectory(_currentPath))
        {
            data = BuildDirectoryListing();
        }
        else if (_currentPath.Count == 3 && _currentPath[0] == "jobs")
        {
            var id = _currentPath[1];
            var file = _currentPath[2];
            if (_globalJobs.TryGetValue(id, out var job))
            {
                if (file == "status") data = Encoding.UTF8.GetBytes(job.Status + "\n");
                else if (file == "output.json") data = Encoding.UTF8.GetBytes(job.Output.ToString());
                else if (file == "errors") data = Encoding.UTF8.GetBytes(job.Errors.ToString());
            }
        }

        if (tread.Offset >= (ulong)data.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        int count = (int)Math.Min((long)tread.Count, data.Length - (long)tread.Offset);
        return new Rread(tread.Tag, data.AsSpan((int)tread.Offset, count).ToArray());
    }

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        if (!IsDirectory(_currentPath))
        {
            throw new NinePNotSupportedException();
        }

        byte[] data = BuildDirectoryListing();
        if (treaddir.Offset >= (ulong)data.Length)
        {
            return Task.FromResult(new Rreaddir(9, treaddir.Tag, 0, ReadOnlyMemory<byte>.Empty));
        }

        int count = (int)Math.Min((long)treaddir.Count, data.Length - (long)treaddir.Offset);
        return Task.FromResult(new Rreaddir(9 + (uint)count, treaddir.Tag, (uint)count, data.AsSpan((int)treaddir.Offset, count).ToArray()));
    }

    private byte[] BuildDirectoryListing()
    {
        var entries = new List<byte>();
        var items = new List<(string Name, QidType Type)>();

        if (_currentPath.Count == 0)
        {
            items.Add(("jobs", QidType.QTDIR));
        }
        else if (_currentPath.Count == 1 && _currentPath[0] == "jobs")
        {
            foreach (var id in _globalJobs.Keys) items.Add((id, QidType.QTDIR));
        }
        else if (_currentPath.Count == 2 && _currentPath[0] == "jobs")
        {
            items.Add(("script.ps1", QidType.QTFILE));
            items.Add(("status", QidType.QTFILE));
            items.Add(("output.json", QidType.QTFILE));
            items.Add(("errors", QidType.QTFILE));
        }

        foreach (var item in items)
        {
            var path = new List<string>(_currentPath) { item.Name };
            var stat = new Stat(0, 0, 0, GetQidForPath(path), 
                item.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644, 
                0, 0, 0, item.Name, "scott", "scott", "scott");
            
            var buf = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(buf, ref offset);
            entries.AddRange(buf);
        }

        return entries.ToArray();
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 3 && _currentPath[0] == "jobs" && _currentPath[2] == "script.ps1")
        {
            var id = _currentPath[1];
            if (_globalJobs.TryGetValue(id, out var job))
            {
                job.Script = Encoding.UTF8.GetString(twrite.Data.ToArray());
                _ = job.RunAsync();
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
        }
        return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "powershell";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQidForPath(_currentPath), 
            0644 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count == 2 && _currentPath[0] == "jobs")
        {
            if (_globalJobs.TryRemove(_currentPath[1], out var job))
            {
                job.Kill();
                return Task.FromResult(new Rremove(tremove.Tag));
            }
        }
        throw new NinePNotSupportedException();
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        return Task.FromResult(new Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, GetQidForPath(_currentPath), 0644));
    }
    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public Task<Rmkdir> MkdirAsync(Tmkdir tmkdir)
    {
        if (_currentPath.Count == 1 && _currentPath[0] == "jobs")
        {
            var job = new PowerShellJob(tmkdir.Name);
            if (_globalJobs.TryAdd(tmkdir.Name, job))
            {
                var path = new List<string>(_currentPath) { tmkdir.Name };
                return Task.FromResult(new Rmkdir(NinePConstants.HeaderSize + 13, tmkdir.Tag, GetQidForPath(path)));
            }
        }
        throw new InvalidOperationException("Cannot create directory here.");
    }

    public INinePFileSystem Clone()
    {
        var clone = new PowerShellFileSystem();
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

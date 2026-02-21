using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StellarDotnetSdk;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using StellarServer = StellarDotnetSdk.Server;

namespace NinePSharp.Server.Backends;

public class StellarFileSystem : INinePFileSystem
{
    private readonly StellarBackendConfig _config;
    private readonly StellarServer? _server;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    public StellarFileSystem(StellarBackendConfig config, StellarServer? server, ILuxVaultService vault)
    {
        _config = config;
        _server = server;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        return false;
    }

    private Qid GetQid(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = string.Join("/", path);
        return new Qid(type, 0, (ulong)pathStr.GetHashCode());
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (!IsDirectory(tempPath))
            {
                if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                break;
            }

            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                tempPath.Add(name);
            }
            qids.Add(GetQid(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        return new Ropen(topen.Tag, GetQid(_currentPath), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[] allData;

        if (_currentPath.Count == 0)
        {
            allData = Encoding.UTF8.GetBytes("balance\nstatus\nnetwork\n");
        }
        else if (_currentPath.Count == 1)
        {
            switch (_currentPath[0])
            {
                case "balance":
                    allData = Encoding.UTF8.GetBytes("0.0 XLM (Mock)\n");
                    break;
                case "status":
                    allData = Encoding.UTF8.GetBytes($"Horizon: {_config.HorizonUrl}\n");
                    break;
                case "network":
                    allData = Encoding.UTF8.GetBytes(_config.UsePublicNetwork ? "Public\n" : "TestNet\n");
                    break;
                default:
                    allData = Array.Empty<byte>();
                    break;
            }
        }
        else {
            allData = Array.Empty<byte>();
        }

        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "stellar";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED : 0x124;
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new StellarFileSystem(_config, _server, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

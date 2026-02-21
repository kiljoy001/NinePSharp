using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.gRPC;

public class GrpcFileSystem : INinePFileSystem
{
    private readonly GrpcBackendConfig _config;
    private readonly IGrpcTransport _transport;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastResponse;

    public GrpcFileSystem(GrpcBackendConfig config, IGrpcTransport transport, ILuxVaultService vault)
    {
        _config = config;
        _transport = transport;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count <= 1) return true;
        return false;
    }

    private Qid GetQid(List<string> path)
    {
        var type = IsDirectory(path) ? QidType.QTDIR : QidType.QTFILE;
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
            _lastResponse = null;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, GetQid(_currentPath), 0));
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset == 0)
        {
            if (_currentPath.Count == 0)
            {
                return new Rread(tread.Tag, Encoding.UTF8.GetBytes("services/\nstatus\n"));
            }
            if (_lastResponse != null)
            {
                return new Rread(tread.Tag, _lastResponse);
            }
        }

        if (_lastResponse != null && tread.Offset < (ulong)_lastResponse.Length)
        {
             var chunk = _lastResponse.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)_lastResponse.Length - (long)tread.Offset)).ToArray();
             return new Rread(tread.Tag, chunk);
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 2)
        {
            var service = _currentPath[0];
            var method = _currentPath[1];
            try {
                _lastResponse = await _transport.CallAsync(service, method, twrite.Data.ToArray());
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) {
                throw new NinePProtocolException($"gRPC call failed: {ex.Message}");
            }
        }
        throw new NotSupportedException("Invalid gRPC path for call.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "grpc";
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new GrpcFileSystem(_config, _transport, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

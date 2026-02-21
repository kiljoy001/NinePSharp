using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends.JsonRpc;

/// <summary>
/// 9P filesystem that translates file reads/writes into explicit, allowlisted JSON-RPC calls.
/// Only endpoints declared in <see cref="JsonRpcBackendConfig.Endpoints"/> are accessible.
/// </summary>
public class JsonRpcFileSystem : INinePFileSystem
{
    private readonly JsonRpcBackendConfig _config;
    private readonly IJsonRpcTransport _transport;
    private List<string> _currentPath = new();
    private string? _lastResponse;

    public JsonRpcFileSystem(JsonRpcBackendConfig config, IJsonRpcTransport transport)
    {
        _config = config;
        _transport = transport;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (_currentPath.Count > 0) _currentPath.RemoveAt(_currentPath.Count - 1);
            }
            else
            {
                _currentPath.Add(name);
            }
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset > 0) return new Rread(tread.Tag, Array.Empty<byte>());

        if (_currentPath.Count == 0)
        {
            var content = string.Join("\n", _config.Endpoints.Select(e => e.Name)) + "\n";
            return new Rread(tread.Tag, Encoding.UTF8.GetBytes(content));
        }

        if (_currentPath.Count == 1)
        {
            var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == _currentPath[0]);
            if (endpoint != null)
            {
                var response = await _transport.CallAsync(endpoint.Method, endpoint.Params.Cast<object>().ToArray());
                return new Rread(tread.Tag, Encoding.UTF8.GetBytes(response + "\n"));
            }
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "jsonrpc";
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new JsonRpcFileSystem(_config, _transport);
        clone._currentPath = new List<string>(_currentPath);
        clone._lastResponse = _lastResponse;
        return clone;
    }
}

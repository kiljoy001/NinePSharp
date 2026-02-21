using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

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
        ulong hash = (ulong)pathStr.GetHashCode();
        return new Qid(type, 0, hash);
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
                if (tempPath.Count == 0)
                {
                    if (!_config.Endpoints.Any(e => e.Name == name))
                    {
                        if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                        break;
                    }
                }
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

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        var qid = GetQid(_currentPath);
        return new Ropen(topen.Tag, qid, 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[] allData;

        if (_currentPath.Count == 0)
        {
            // Root directory listing - only at offset 0
            if (tread.Offset > 0) {
                // Technically Tread could be mid-directory, but for now we generate full and slice
            }
            var content = string.Join("\n", _config.Endpoints.Select(e => e.Name)) + "\n";
            allData = Encoding.UTF8.GetBytes(content);
        }
        else if (_currentPath.Count == 1)
        {
            // File read
            if (_lastResponse == null)
            {
                // ONLY trigger the RPC call if Offset is 0. 
                // Subsequent reads with Offset > 0 will use the cached _lastResponse.
                if (tread.Offset != 0) {
                    return new Rread(tread.Tag, Array.Empty<byte>());
                }

                var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == _currentPath[0]);
                if (endpoint != null)
                {
                    try {
                        var response = await _transport.CallAsync(endpoint.Method, endpoint.Params.Cast<object?>().ToArray());
                        _lastResponse = response?.ToString() ?? "null";
                        _lastResponse += "\n";
                    }
                    catch (Exception ex) {
                        _lastResponse = $"Error: {ex.Message}\n";
                    }
                }
                else {
                    _lastResponse = "Endpoint not found.\n";
                }
            }
            allData = Encoding.UTF8.GetBytes(_lastResponse);
        }
        else {
            allData = Array.Empty<byte>();
        }

        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 1)
        {
            var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == _currentPath[0]);
            if (endpoint != null)
            {
                if (!endpoint.Writable) {
                    // Test expects return Count=0 for read-only write attempts
                    return new Rwrite(twrite.Tag, 0);
                }

                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                try {
                    object?[]? args;
                    if (input.StartsWith("[") && input.EndsWith("]")) {
                        args = JsonSerializer.Deserialize<object?[]>(input);
                    }
                    else {
                        args = new object?[] { input };
                    }

                    var response = await _transport.CallAsync(endpoint.Method, args);
                    _lastResponse = response?.ToString() ?? "null";
                    _lastResponse += "\n";
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
                catch (Exception ex) {
                    throw new NinePProtocolException($"Write to JSON-RPC failed: {ex.Message}");
                }
            }
        }
        throw new NinePProtocolException("Invalid write target.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "jsonrpc";
        bool isDir = IsDirectory(_currentPath);
        
        // Mode bits: DMDIR (0x80000000)
        uint mode = 0;
        if (isDir) {
            mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED; // 0755
        }
        else {
            var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == name);
            // 0666 octal = 0x1B6
            // 0444 octal = 0x124
            mode = (endpoint != null && endpoint.Writable) ? (uint)0x1B6 : (uint)0x124;
        }

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott");
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

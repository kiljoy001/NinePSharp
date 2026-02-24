using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Buffers.Binary;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.JsonRpc;

public class JsonRpcFileSystem : INinePFileSystem
{
    private readonly JsonRpcBackendConfig _config;
    private readonly IJsonRpcTransport _transport;
    private List<string> _currentPath = new();
    private string? _lastResponse;

    public bool DotU { get; set; }

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
                    if (!_config.Endpoints.Any(e => e.Name == name) && name != "status")
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

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type, bool Writable)>();
            
            foreach (var endpoint in _config.Endpoints)
            {
                files.Add((endpoint.Name, QidType.QTFILE, endpoint.Writable));
            }

            if (_currentPath.Count == 0)
            {
                files.Add(("status", QidType.QTFILE, false));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : (f.Writable ? (uint)0x1B6 : (uint)0x124);
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dotu: DotU);
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }

            allData = entries.ToArray();
            
            int totalToSend = 0;
            int currentOffset = (int)tread.Offset;
            while (currentOffset + 2 <= allData.Length)
            {
                ushort entrySize = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2);
                if (totalToSend + entrySize > tread.Count) break;
                totalToSend += entrySize;
                currentOffset += entrySize;
            }
            
            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }
        else if (_currentPath.Count == 1)
        {
            if (_lastResponse == null)
            {
                if (tread.Offset != 0) return new Rread(tread.Tag, Array.Empty<byte>());

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
                else if (_currentPath[0] == "status")
                {
                    _lastResponse = $"JSON-RPC Backend: {_config.EndpointUrl}\nEndpoints: {_config.Endpoints.Count}\n";
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
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");

        if (_currentPath.Count == 1)
        {
            var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == _currentPath[0]);
            if (endpoint != null)
            {
                if (!endpoint.Writable) return new Rwrite(twrite.Tag, 0);

                string input = Encoding.UTF8.GetString(twrite.Data.ToArray()).Trim();
                try {
                    object?[]? args;
                    if (input.StartsWith("[") && input.EndsWith("]")) args = JsonSerializer.Deserialize<object?[]>(input);
                    else args = new object?[] { input };

                    var response = await _transport.CallAsync(endpoint.Method, args);
                    _lastResponse = (response?.ToString() ?? "null") + "\n";
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
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1ED : 0x124;
        if (!isDir) {
            var endpoint = _config.Endpoints.FirstOrDefault(e => e.Name == name);
            if (endpoint != null && endpoint.Writable) mode = 0x1B6;
        }

        var stat = new Stat(0, 0, 0, GetQid(_currentPath), mode, 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePNotSupportedException();

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var qid = new Qid(QidType.QTDIR, 0, 0);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new JsonRpcFileSystem(_config, _transport);
        clone._currentPath = new List<string>(_currentPath);
        clone._lastResponse = _lastResponse;
        clone.DotU = DotU;
        return clone;
    }
}

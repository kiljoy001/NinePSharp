using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.REST;

/// <summary>
/// Provides a 9P filesystem interface to RESTful APIs.
/// Translates directory walks and file operations into HTTP requests and responses.
/// </summary>
public class RestFileSystem : INinePFileSystem
{
    private readonly RestBackendConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;
    private HttpMethod _currentMethod = HttpMethod.Get;
    private string _targetResource = "";
    private Dictionary<string, string> _headers = new();
    private Dictionary<string, string> _params = new();

    public bool DotU { get; set; }

    public RestFileSystem(RestBackendConfig config, HttpClient httpClient, ILuxVaultService vault)
    {
        _config = config;
        _httpClient = httpClient;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        var last = path.Last().ToLowerInvariant();
        if (last == "get" || last == "post" || last == "put" || last == "delete" || last == ".headers" || last == ".params" || last == "status") return false;
        return true; 
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
                if (!IsDirectory(tempPath)) {
                    if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>()); 
                    break;
                }
                tempPath.Add(name);
            }
            qids.Add(GetQid(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
            if (_currentPath.Count > 0)
            {
                var last = _currentPath.Last().ToLowerInvariant();
                switch (last)
                {
                    case "get": _currentMethod = HttpMethod.Get; break;
                    case "post": _currentMethod = HttpMethod.Post; break;
                    case "put": _currentMethod = HttpMethod.Put; break;
                    case "delete": _currentMethod = HttpMethod.Delete; break;
                    default: _currentMethod = HttpMethod.Get; break;
                }

                if (!IsDirectory(_currentPath)) _targetResource = string.Join("/", _currentPath.Take(_currentPath.Count - 1));
                else _targetResource = string.Join("/", _currentPath);
            }
            _lastReadData = null; // Invalidate cache on walk
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        _lastReadData = null; // Invalidate cache on open to ensure fresh data
        return Task.FromResult(new Ropen(topen.Tag, GetQid(_currentPath), 0));
    }

    private string BuildUrl()
    {
        var resource = _targetResource.TrimStart('/');
        var baseUrl = _config.BaseUrl.TrimEnd('/');
        var fullUrl = string.IsNullOrEmpty(resource) ? baseUrl : $"{baseUrl}/{resource}";

        if (_params.Count == 0) return fullUrl;
        var query = string.Join("&", _params.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return fullUrl.Contains("?") ? $"{fullUrl}&{query}" : $"{fullUrl}?{query}";
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new[] { "get", "post", "put", "delete", ".headers", ".params", "status" };
            foreach (var f in files)
            {
                var qid = new Qid(QidType.QTFILE, 0, (ulong)f.GetHashCode());
                uint mode = (f == ".headers" || f == ".params" || (f != "status" && f != "get")) ? (uint)0x1B6 : (uint)0x124;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f, "scott", "scott", "scott", dotu: DotU);
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }
            var allData = entries.ToArray();
            int totalToSend = 0;
            int curOff = (int)tread.Offset;
            while (curOff + 2 <= allData.Length)
            {
                ushort sz = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(curOff, 2)) + 2);
                if (totalToSend + sz > tread.Count) break;
                totalToSend += sz;
                curOff += sz;
            }
            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }

        if (tread.Offset == 0 || _lastReadData == null)
        {
            var last = _currentPath.Last().ToLowerInvariant();
            string result = "";
            if (last == ".headers") {
                var sb = new StringBuilder();
                foreach (var h in _headers) sb.AppendLine($"{h.Key}: {h.Value}");
                result = sb.ToString();
            }
            else if (last == ".params") {
                var sb = new StringBuilder();
                foreach (var p in _params) sb.AppendLine($"{p.Key}={p.Value}");
                result = sb.ToString();
            }
            else if (last == "status") result = $"REST Backend: {_config.BaseUrl}\nResource: /{_targetResource.TrimStart('/')}\nMethod: {_currentMethod}\n";
            else if (last == "get") {
                try {
                    using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl());
                    foreach (var h in _headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    var response = await _httpClient.SendAsync(request);
                    result = await response.Content.ReadAsStringAsync() + "\n";
                } catch (Exception ex) { result = $"Error: {ex.Message}\n"; }
            }
            _lastReadData = Encoding.UTF8.GetBytes(result);
        }

        if (_lastReadData == null || tread.Offset >= (ulong)_lastReadData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = _lastReadData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)_lastReadData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");
        _lastReadData = null; // Invalidate cache

        var last = _currentPath.Last().ToLowerInvariant();
        if (last == ".headers")
        {
            var content = Encoding.UTF8.GetString(twrite.Data.ToArray());
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines) {
                var parts = line.Split(':', 2);
                if (parts.Length == 2) _headers[parts[0].Trim()] = parts[1].Trim();
            }
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        if (last == ".params")
        {
            var content = Encoding.UTF8.GetString(twrite.Data.ToArray());
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines) {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) _params[parts[0].Trim()] = parts[1].Trim();
            }
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }

        var bodyBytes = twrite.Data.Span;
        char[] bodyChars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bodyBytes), pinned: true);
        try {
            Encoding.UTF8.GetChars(bodyBytes, bodyChars);
            string json = new string(bodyChars);
            try {
                using var request = new HttpRequestMessage(_currentMethod, BuildUrl());
                if (_currentMethod != HttpMethod.Get && _currentMethod != HttpMethod.Delete) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                foreach (var h in _headers) request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                var response = await _httpClient.SendAsync(request);
                // Invalidate cache so subsequent READ sees the effect if applicable
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) { throw new NinePProtocolException($"REST {_currentMethod} failed: {ex.Message}"); }
        }
        finally { Array.Clear(bodyChars); }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "rest";
        bool isDir = IsDirectory(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
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
        var clone = new RestFileSystem(_config, _httpClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._headers = new Dictionary<string, string>(_headers);
        clone._params = new Dictionary<string, string>(_params);
        clone._targetResource = _targetResource;
        clone._currentMethod = _currentMethod;
        clone.DotU = DotU;
        return clone;
    }
}

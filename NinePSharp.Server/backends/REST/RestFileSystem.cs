using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.REST;

public class RestFileSystem : INinePFileSystem
{
    private readonly RestBackendConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private string? _lastResponse;

    public RestFileSystem(RestBackendConfig config, HttpClient httpClient, ILuxVaultService vault)
    {
        _config = config;
        _httpClient = httpClient;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
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
        if (tread.Offset == 0 && _currentPath.Count > 0)
        {
            var relativeUrl = string.Join("/", _currentPath);
            try {
                var response = await _httpClient.GetStringAsync(relativeUrl);
                _lastResponse = response + "\n";
            }
            catch (Exception ex) {
                _lastResponse = $"Error: {ex.Message}\n";
            }
        }
        else if (tread.Offset == 0 && _currentPath.Count == 0)
        {
            _lastResponse = "endpoints/\n";
        }

        byte[] allData = Encoding.UTF8.GetBytes(_lastResponse ?? "");
        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count > 0)
        {
            var relativeUrl = string.Join("/", _currentPath);
            var content = new StringContent(Encoding.UTF8.GetString(twrite.Data.ToArray()), Encoding.UTF8, "application/json");
            
            try {
                var response = await _httpClient.PostAsync(relativeUrl, content);
                _lastResponse = await response.Content.ReadAsStringAsync() + "\n";
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) {
                throw new NinePProtocolException($"REST write failed: {ex.Message}");
            }
        }
        throw new NinePProtocolException("Cannot write to root.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "rest";
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new RestFileSystem(_config, _httpClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

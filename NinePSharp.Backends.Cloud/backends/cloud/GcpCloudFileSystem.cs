using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using Google.Cloud.SecretManager.V1;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class GcpCloudFileSystem : INinePFileSystem
{
    private readonly GcpBackendConfig _config;
    private readonly StorageClient? _storageClient;
    private readonly SecretManagerServiceClient? _secretsClient;
    private readonly ILuxVaultService _vault;
    
    private List<string> _currentPath = new();
    private INinePFileSystem? _activeSubFs;

    public GcpCloudFileSystem(GcpBackendConfig config, StorageClient? storage, SecretManagerServiceClient? secrets, ILuxVaultService vault)
    {
        _config = config;
        _storageClient = storage;
        _secretsClient = secrets;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
        if (_activeSubFs != null) return await _activeSubFs.WalkAsync(twalk);

        var first = twalk.Wname[0];
        if (first == "storage" && _storageClient != null)
        {
            _activeSubFs = new GcpStorageFileSystem(_config, _storageClient, _vault);
            return await _activeSubFs.WalkAsync(new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray()));
        }
        if (first == "secrets" && _secretsClient != null)
        {
            _activeSubFs = new GcpSecretsFileSystem(_config, _secretsClient, _vault);
            return await _activeSubFs.WalkAsync(new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray()));
        }

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);
        foreach (var name in twalk.Wname)
        {
            if (name == "..") { if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1); }
            else tempPath.Add(name);
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        
        if (qids.Count == twalk.Wname.Length) _currentPath = tempPath;
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen) => _activeSubFs?.OpenAsync(topen) ?? Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0));
    
    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_activeSubFs != null) return await _activeSubFs.ReadAsync(tread);

        if (_currentPath.Count == 0)
        {
            var entries = new List<byte>();
            var files = new[] { ("storage", QidType.QTDIR), ("secrets", QidType.QTDIR) };
            
            foreach (var f in files)
            {
                var qid = new Qid(f.Item2, 0, (ulong)f.Item1.GetHashCode());
                var mode = (uint)NinePConstants.FileMode9P.DMDIR | 0755;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Item1, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }

            var allData = entries.ToArray();
            if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
            var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
            return new Rread(tread.Tag, chunk);
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => _activeSubFs?.WriteAsync(twrite) ?? throw new NinePNotSupportedException();
    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => _activeSubFs?.ClunkAsync(tclunk) ?? Task.FromResult(new Rclunk(tclunk.Tag));
    
    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        if (_activeSubFs != null) return await _activeSubFs.StatAsync(tstat);
        
        var name = _currentPath.LastOrDefault() ?? "gcp";
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }
    
    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new GcpCloudFileSystem(_config, _storageClient, _secretsClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._activeSubFs = _activeSubFs?.Clone();
        return clone;
    }
}

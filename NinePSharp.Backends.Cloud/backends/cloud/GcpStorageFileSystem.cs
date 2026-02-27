using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

/// <summary>
/// Provides a 9P filesystem interface to Google Cloud Storage.
/// Maps buckets to top-level directories and GCS objects to files.
/// </summary>
public class GcpStorageFileSystem : INinePFileSystem
{
    private readonly GcpBackendConfig _config;
    private readonly StorageClient _storageClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;

    public GcpStorageFileSystem(GcpBackendConfig config, StorageClient storageClient, ILuxVaultService vault)
    {
        _config = config;
        _storageClient = storageClient;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        return path.Count <= 1; 
    }

    private Qid GetQid(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = "storage/" + string.Join("/", path);
        return new Qid(type, 0, (ulong)pathStr.GetHashCode());
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (name == "..") { if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1); }
            else tempPath.Add(name);
            qids.Add(GetQid(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
            _lastReadData = null;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, GetQid(_currentPath), 0));

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[] allData;

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type)>();

            if (_currentPath.Count == 0)
            {
                var buckets = _storageClient.ListBuckets(_config.ProjectId);
                foreach (var b in buckets) files.Add((b.Name, QidType.QTDIR));
            }
            else if (_currentPath.Count == 1)
            {
                var objects = _storageClient.ListObjects(_currentPath[0]);
                foreach (var obj in objects) files.Add((obj.Name, QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }
            allData = entries.ToArray();
        }
        else if (tread.Offset == 0)
        {
            var bucketName = _currentPath[0];
            var objectName = string.Join("/", _currentPath.Skip(1));
            try {
                using var ms = new MemoryStream();
                await _storageClient.DownloadObjectAsync(bucketName, objectName, ms);
                _lastReadData = ms.ToArray();
            } catch (Exception ex) {
                _lastReadData = Encoding.UTF8.GetBytes($"Error: {ex.Message}\n");
            }
            allData = _lastReadData;
        }
        else
        {
            allData = _lastReadData ?? Array.Empty<byte>();
        }

        if (allData == null || tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count < 2) throw new NinePProtocolException("Cannot write to root or bucket level.");

        var bucketName = _currentPath[0];
        var objectName = string.Join("/", _currentPath.Skip(1));

        try {
            using var ms = new MemoryStream(twrite.Data.ToArray());
            await _storageClient.UploadObjectAsync(bucketName, objectName, null, ms);
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        catch (Exception ex) {
            throw new NinePProtocolException($"GCP Storage Upload failed: {ex.Message}");
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "storage";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public async Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count == 0) throw new NinePProtocolException("Cannot remove root.");
        
        var bucketName = _currentPath[0];
        if (_currentPath.Count == 1)
        {
            await _storageClient.DeleteBucketAsync(bucketName);
        }
        else
        {
            var objectName = string.Join("/", _currentPath.Skip(1));
            await _storageClient.DeleteObjectAsync(bucketName, objectName);
        }
        
        return new Rremove(tremove.Tag);
    }

    public INinePFileSystem Clone()
    {
        var clone = new GcpStorageFileSystem(_config, _storageClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

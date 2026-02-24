using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

/// <summary>
/// Provides a 9P filesystem interface to Azure Blob Storage.
/// Maps containers to top-level directories and blobs to files.
/// </summary>
public class AzureBlobsFileSystem : INinePFileSystem
{
    private readonly AzureBackendConfig _config;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;

    public bool DotU { get; set; }

    public AzureBlobsFileSystem(AzureBackendConfig config, BlobServiceClient blobServiceClient, ILuxVaultService vault)
    {
        _config = config;
        _blobServiceClient = blobServiceClient;
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
        var pathStr = "blobs/" + string.Join("/", path);
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
                await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
                    files.Add((container.Name, QidType.QTDIR));
            }
            else if (_currentPath.Count == 1)
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_currentPath[0]);
                await foreach (var blob in containerClient.GetBlobsAsync())
                    files.Add((blob.Name, QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dotu: DotU);
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }
            allData = entries.ToArray();
        }
        else if (tread.Offset == 0)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_currentPath[0]);
            var blobClient = containerClient.GetBlobClient(string.Join("/", _currentPath.Skip(1)));
            try {
                var download = await blobClient.DownloadContentAsync();
                _lastReadData = download.Value.Content.ToArray();
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
        if (_currentPath.Count < 2) throw new NinePProtocolException("Cannot write to root or container level.");

        var containerClient = _blobServiceClient.GetBlobContainerClient(_currentPath[0]);
        var blobClient = containerClient.GetBlobClient(string.Join("/", _currentPath.Skip(1)));

        try {
            using var ms = new MemoryStream(twrite.Data.ToArray());
            await blobClient.UploadAsync(ms, overwrite: true);
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        catch (Exception ex) {
            throw new NinePProtocolException($"Azure Blob Upload failed: {ex.Message}");
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "blobs";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public async Task<Rremove> RemoveAsync(Tremove tremove)
    {
        if (_currentPath.Count == 0) throw new NinePProtocolException("Cannot remove root.");
        
        var containerName = _currentPath[0];
        if (_currentPath.Count == 1)
        {
            await _blobServiceClient.DeleteBlobContainerAsync(containerName);
        }
        else
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobName = string.Join("/", _currentPath.Skip(1));
            await containerClient.DeleteBlobAsync(blobName);
        }
        
        return new Rremove(tremove.Tag);
    }

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        bool isDir = IsDirectory(_currentPath);
        var qid = GetQid(_currentPath);
        uint mode = isDir ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new AzureBlobsFileSystem(_config, _blobServiceClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = DotU;
        return clone;
    }
}

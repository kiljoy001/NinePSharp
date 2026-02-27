using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Security.KeyVault.Secrets;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class AzureCloudFileSystem : INinePFileSystem
{
    private readonly AzureBackendConfig _config;
    private readonly BlobServiceClient? _blobClient;
    private readonly SecretClient? _secretClient;
    private readonly ILuxVaultService _vault;
    
    private List<string> _currentPath = new();
    private INinePFileSystem? _activeSubFs;

    public AzureCloudFileSystem(AzureBackendConfig config, BlobServiceClient? blobs, SecretClient? secrets, ILuxVaultService vault)
    {
        _config = config;
        _blobClient = blobs;
        _secretClient = secrets;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
        if (_activeSubFs != null) return await _activeSubFs.WalkAsync(twalk);

        var first = twalk.Wname[0];
        if (first == "blobs" && _blobClient != null)
        {
            _activeSubFs = new AzureBlobsFileSystem(_config, _blobClient, _vault);
            return await _activeSubFs.WalkAsync(new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray()));
        }
        if (first == "secrets" && _secretClient != null)
        {
            _activeSubFs = new AzureSecretsFileSystem(_config, _secretClient, _vault);
            return await _activeSubFs.WalkAsync(new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray()));
        }

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);
        foreach (var name in twalk.Wname)
        {
            if (name == "..") { if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1); }
            else tempPath.Add(name);
            qids.Add(new Qid(IsDirectory(tempPath) ? QidType.QTDIR : QidType.QTFILE, 0, (ulong)name.GetHashCode()));
        }
        
        if (qids.Count == twalk.Wname.Length) _currentPath = tempPath;
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        return false;
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        if (_activeSubFs != null) return await _activeSubFs.OpenAsync(topen);
        return new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_activeSubFs != null) return await _activeSubFs.ReadAsync(tread);

        if (_currentPath.Count == 0)
        {
            var entries = new List<byte>();
            var files = new[] { ("blobs", QidType.QTDIR), ("secrets", QidType.QTDIR) };
            
            foreach (var f in files)
            {
                var qid = new Qid(f.Item2, 0, (ulong)f.Item1.GetHashCode());
                var mode = (uint)NinePConstants.FileMode9P.DMDIR | 0755;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Item1, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }

            var allData = entries.ToArray();
            
            int totalToSend = 0;
            int currentOffset = (int)tread.Offset;
            while (currentOffset + 2 <= allData.Length)
            {
                ushort entrySize = (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2);
                if (totalToSend + entrySize > tread.Count) break;
                totalToSend += entrySize;
                currentOffset += entrySize;
            }
            
            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_activeSubFs != null) return await _activeSubFs.WriteAsync(twrite);
        throw new NinePNotSupportedException();
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        if (_activeSubFs != null) return await _activeSubFs.ClunkAsync(tclunk);
        return new Rclunk(tclunk.Tag);
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        if (_activeSubFs != null) return await _activeSubFs.StatAsync(tstat);
        
        var name = _currentPath.LastOrDefault() ?? "azure";
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }
    
    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new AzureCloudFileSystem(_config, _blobClient, _secretClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._activeSubFs = _activeSubFs?.Clone();
        return clone;
    }
}

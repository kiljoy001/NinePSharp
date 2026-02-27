using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.SecretsManager;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class AwsCloudFileSystem : INinePFileSystem
{
    private readonly AwsBackendConfig _config;
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSecretsManager _secretsClient;
    private readonly ILuxVaultService _vault;
    
    private List<string> _currentPath = new();
    private INinePFileSystem? _activeSubFs;

    public AwsCloudFileSystem(AwsBackendConfig config, IAmazonS3 s3, IAmazonSecretsManager secrets, ILuxVaultService vault)
    {
        _config = config;
        _s3Client = s3;
        _secretsClient = secrets;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
        if (_activeSubFs != null) return await _activeSubFs.WalkAsync(twalk);

        var first = twalk.Wname[0];
        if (first == "s3")
        {
            _activeSubFs = new AwsS3FileSystem(_config, _s3Client, _vault);
            return await _activeSubFs.WalkAsync(new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray()));
        }
        if (first == "secrets")
        {
            _activeSubFs = new AwsSecretsFileSystem(_config, _secretsClient, _vault);
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

    public async Task<Ropen> OpenAsync(Topen topen) => await (_activeSubFs?.OpenAsync(topen) ?? Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0)));
    public async Task<Rread> ReadAsync(Tread tread) => await (_activeSubFs?.ReadAsync(tread) ?? Task.FromResult(new Rread(tread.Tag, System.Text.Encoding.UTF8.GetBytes("s3/\nsecrets/\n"))));
    public async Task<Rwrite> WriteAsync(Twrite twrite) => await (_activeSubFs?.WriteAsync(twrite) ?? throw new NinePNotSupportedException());
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => await (_activeSubFs?.ClunkAsync(tclunk) ?? Task.FromResult(new Rclunk(tclunk.Tag)));
    public async Task<Rstat> StatAsync(Tstat tstat) => await (_activeSubFs?.StatAsync(tstat) ?? Task.FromResult(new Rstat(tstat.Tag, new Stat(0,0,0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0,0,0, "aws", "scott", "scott", "scott"))));
    
    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NinePNotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NinePNotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new AwsCloudFileSystem(_config, _s3Client, _secretsClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._activeSubFs = _activeSubFs?.Clone();
        return clone;
    }
}

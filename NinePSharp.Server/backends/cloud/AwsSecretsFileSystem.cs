using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class AwsSecretsFileSystem : INinePFileSystem
{
    private readonly AwsBackendConfig _config;
    private readonly IAmazonSecretsManager _secretsClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;

    public bool DotU { get; set; }

    public AwsSecretsFileSystem(AwsBackendConfig config, IAmazonSecretsManager secretsClient, ILuxVaultService vault)
    {
        _config = config;
        _secretsClient = secretsClient;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        return path.Count == 0; 
    }

    private Qid GetQid(List<string> path)
    {
        bool isDir = IsDirectory(path);
        var type = isDir ? QidType.QTDIR : QidType.QTFILE;
        var pathStr = "secrets/" + string.Join("/", path);
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
        if (tread.Offset == 0)
        {
            if (_currentPath.Count == 0)
            {
                var response = await _secretsClient.ListSecretsAsync(new ListSecretsRequest());
                var sb = new StringBuilder();
                foreach (var s in response.SecretList) sb.AppendLine(s.Name);
                _lastReadData = Encoding.UTF8.GetBytes(sb.ToString());
            }
            else
            {
                var secretId = _currentPath[0];
                try {
                    var response = await _secretsClient.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId });
                    _lastReadData = Encoding.UTF8.GetBytes(response.SecretString ?? "");
                } catch (Exception ex) {
                    _lastReadData = Encoding.UTF8.GetBytes($"Error: {ex.Message}\n");
                }
            }
        }

        if (_lastReadData == null || tread.Offset >= (ulong)_lastReadData.Length) 
            return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = _lastReadData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)_lastReadData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (_currentPath.Count == 0) throw new NinePProtocolException("Cannot write to root secrets directory.");

        var secretId = _currentPath[0];
        var newValue = Encoding.UTF8.GetString(twrite.Data.ToArray());

        try {
            await _secretsClient.PutSecretValueAsync(new PutSecretValueRequest { SecretId = secretId, SecretString = newValue });
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        catch (Exception ex) {
            throw new NinePProtocolException($"AWS Secrets Update failed: {ex.Message}");
        }
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "secrets";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new AwsSecretsFileSystem(_config, _secretsClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = DotU;
        return clone;
    }
}

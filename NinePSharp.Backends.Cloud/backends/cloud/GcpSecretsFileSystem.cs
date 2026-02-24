using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.SecretManager.V1;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.Cloud;

public class GcpSecretsFileSystem : INinePFileSystem
{
    private readonly GcpBackendConfig _config;
    private readonly SecretManagerServiceClient _secretsClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;

    public bool DotU { get; set; }

    public GcpSecretsFileSystem(GcpBackendConfig config, SecretManagerServiceClient secretsClient, ILuxVaultService vault)
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
        byte[] allData;

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type)>();

            if (_currentPath.Count == 0)
            {
                var projectName = new ProjectName(_config.ProjectId);
                var secrets = _secretsClient.ListSecrets(projectName);
                foreach (var s in secrets) files.Add((s.SecretName.SecretId, QidType.QTFILE));
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
            var secretId = _currentPath[0];
            var secretVersionName = new SecretVersionName(_config.ProjectId, secretId, "latest");
            try {
                var response = await _secretsClient.AccessSecretVersionAsync(secretVersionName);
                _lastReadData = response.Payload.Data.ToByteArray();
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
        if (_currentPath.Count == 0) throw new NinePProtocolException("Cannot write to root secrets directory.");

        var secretId = _currentPath[0];
        var secretName = new SecretName(_config.ProjectId, secretId);
        var payload = new SecretPayload { Data = Google.Protobuf.ByteString.CopyFrom(twrite.Data.ToArray()) };

        try {
            await _secretsClient.AddSecretVersionAsync(secretName, payload);
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }
        catch (Exception ex) {
            throw new NinePProtocolException($"GCP Secret Update failed: {ex.Message}");
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
        var clone = new GcpSecretsFileSystem(_config, _secretsClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = DotU;
        return clone;
    }
}

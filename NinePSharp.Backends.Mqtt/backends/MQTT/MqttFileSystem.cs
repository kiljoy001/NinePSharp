using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends.MQTT;

public class MqttFileSystem : INinePFileSystem
{
    private readonly MqttBackendConfig _config;
    private readonly IMqttTransport _transport;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    public bool DotU { get; set; }

    public MqttFileSystem(MqttBackendConfig config, IMqttTransport transport, ILuxVaultService vault)
    {
        _config = config;
        _transport = transport;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0].ToLowerInvariant() == "topics")
        {
            // Root topics dir is a directory, but specific topics are treated as files
            return path.Count == 1; 
        }
        return false;
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
            if (name == "..") { if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1); }
            else tempPath.Add(name);
            qids.Add(GetQid(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
            
            // Auto-subscribe when walking into a topic
            if (_currentPath.Count > 1 && _currentPath[0].ToLowerInvariant() == "topics")
            {
                var topic = string.Join("/", _currentPath.Skip(1));
                await _transport.SubscribeAsync(topic);
            }
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, GetQid(_currentPath), 0));
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        byte[]? dataToRead = null;

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type)>();

            if (_currentPath.Count == 0)
            {
                files.Add(("topics", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
            }
            // Mqtt topics are dynamic, so listing /topics/ is normally empty 
            // unless we tracked active subscriptions. For now we keep it simple.

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
            dataToRead = entries.ToArray();
        }
        else if (tread.Offset == 0)
        {
            if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "status")
            {
                dataToRead = Encoding.UTF8.GetBytes($"MQTT Backend: {_config.BrokerUrl}\nClient ID: {_config.ClientId}\nConnected: {_transport.IsConnected}\n");
            }
            else if (_currentPath.Count > 1 && _currentPath[0].ToLowerInvariant() == "topics")
            {
                var topic = string.Join("/", _currentPath.Skip(1));
                dataToRead = await _transport.GetNextMessageAsync(topic);
            }
        }

        if (dataToRead == null || tread.Offset >= (ulong)dataToRead.Length)
            return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = dataToRead.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)dataToRead.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");

        if (_currentPath.Count > 1 && _currentPath[0].ToLowerInvariant() == "topics")
        {
            var topic = string.Join("/", _currentPath.Skip(1));
            
            // Hardening: use pinned buffer for payload
            var payloadBytes = twrite.Data.ToArray();
            byte[] pinnedPayload = GC.AllocateArray<byte>(payloadBytes.Length, pinned: true);
            payloadBytes.CopyTo(pinnedPayload, 0);
            Array.Clear(payloadBytes);

            try {
                await _transport.PublishAsync(topic, pinnedPayload);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) {
                throw new NinePProtocolException($"MQTT publish failed: {ex.Message}");
            }
            finally {
                Array.Clear(pinnedPayload);
            }
        }

        throw new NotSupportedException("Invalid MQTT path for write.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "mqtt";
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new MqttFileSystem(_config, _transport, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

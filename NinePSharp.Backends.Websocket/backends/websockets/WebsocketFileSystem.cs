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

namespace NinePSharp.Server.Backends.Websockets;

public class WebsocketFileSystem : INinePFileSystem
{
    private readonly WebsocketBackendConfig _config;
    private readonly IWebsocketTransport _transport;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastReadData;

    public bool DotU { get; set; }

    public WebsocketFileSystem(WebsocketBackendConfig config, IWebsocketTransport transport, ILuxVaultService vault)
    {
        _config = config;
        _transport = transport;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
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
            if (_lastReadData != null) {
                Array.Clear(_lastReadData);
                _lastReadData = null;
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
            var files = new[] { "data", "status" };
            
            foreach (var f in files)
            {
                var qid = new Qid(QidType.QTFILE, 0, (ulong)f.GetHashCode());
                var stat = new Stat(0, 0, 0, qid, 0644, 0, 0, 0, f, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }
            dataToRead = entries.ToArray();
        }
        else if (tread.Offset == 0)
        {
            if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "data")
            {
                dataToRead = await _transport.GetNextMessageAsync();
                if (dataToRead != null)
                {
                    if (_lastReadData != null) Array.Clear(_lastReadData);
                    _lastReadData = GC.AllocateArray<byte>(dataToRead.Length, pinned: true);
                    dataToRead.CopyTo(_lastReadData, 0);
                    Array.Clear(dataToRead); // Zero original buffer
                    dataToRead = _lastReadData;
                }
            }
            else if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "status")
            {
                dataToRead = Encoding.UTF8.GetBytes($"WebSocket Backend: {_config.Url}\nConnected: {_transport.IsConnected}\n");
            }
        }
        else if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "data")
        {
            dataToRead = _lastReadData;
        }

        if (dataToRead == null || tread.Offset >= (ulong)dataToRead.Length)
            return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = dataToRead.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)dataToRead.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");

        if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "data")
        {
            // Hardening: use pinned buffer for payload
            var payloadBytes = twrite.Data.ToArray();
            byte[] pinnedPayload = GC.AllocateArray<byte>(payloadBytes.Length, pinned: true);
            payloadBytes.CopyTo(pinnedPayload, 0);
            Array.Clear(payloadBytes);

            try {
                await _transport.SendAsync(pinnedPayload);
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) {
                throw new NinePProtocolException($"WebSocket send failed: {ex.Message}");
            }
            finally {
                Array.Clear(pinnedPayload);
            }
        }

        throw new NotSupportedException("Invalid WebSocket path for write.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        if (_lastReadData != null) {
            Array.Clear(_lastReadData);
            _lastReadData = null;
        }
        return new Rclunk(tclunk.Tag);
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "ws";
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
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
        var clone = new WebsocketFileSystem(_config, _transport, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}

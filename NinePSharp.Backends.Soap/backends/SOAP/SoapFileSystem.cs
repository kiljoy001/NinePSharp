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

namespace NinePSharp.Server.Backends.SOAP;

public class SoapFileSystem : INinePFileSystem
{
    private readonly SoapBackendConfig _config;
    private readonly ISoapTransport _transport;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastResponseBytes;
    private Dictionary<string, string> _headers = new();

    public bool DotU { get; set; }

    public SoapFileSystem(SoapBackendConfig config, ISoapTransport transport, ILuxVaultService vault)
    {
        _config = config;
        _transport = transport;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        var last = path.Last().ToLowerInvariant();
        if (last == ".headers") return false;
        if (path[0].ToLowerInvariant() == "actions" && path.Count == 1) return true;
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
            // Clear last response on navigation
            if (_lastResponseBytes != null) {
                Array.Clear(_lastResponseBytes);
                _lastResponseBytes = null;
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
        byte[] allData;

        if (IsDirectory(_currentPath))
        {
            var entries = new List<byte>();
            var files = new List<(string Name, QidType Type)>();
            
            if (_currentPath.Count == 0)
            {
                files.Add(("actions", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
                files.Add((".headers", QidType.QTFILE));
            }
            else if (_currentPath.Count == 1 && _currentPath[0] == "actions")
            {
                files.Add(("Call", QidType.QTFILE));
            }

            foreach (var f in files)
            {
                var qid = new Qid(f.Type, 0, (ulong)f.Name.GetHashCode());
                var mode = f.Type == QidType.QTDIR ? (uint)NinePConstants.FileMode9P.DMDIR | 0755 : 0644;
                var stat = new Stat(0, 0, 0, qid, mode, 0, 0, 0, f.Name, "scott", "scott", "scott", dotu: DotU);
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }
            allData = entries.ToArray();

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
        else if (tread.Offset == 0)
        {
            if (_currentPath.Count == 1 && _currentPath[0] == ".headers")
            {
                var sb = new StringBuilder();
                foreach (var h in _headers) sb.AppendLine($"{h.Key}: {h.Value}");
                _lastResponseBytes = Encoding.UTF8.GetBytes(sb.ToString());
            }
            else if (_currentPath.Count == 1 && _currentPath[0] == "status")
            {
                _lastResponseBytes = Encoding.UTF8.GetBytes($"SOAP Backend: {_config.WsdlUrl}\n");
            }
            allData = _lastResponseBytes ?? Array.Empty<byte>();
        }
        else
        {
            allData = _lastResponseBytes ?? Array.Empty<byte>();
        }

        if (allData == null || tread.Offset >= (ulong)allData.Length)
            return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = allData.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)allData.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");

        if (_currentPath.Count > 0 && _currentPath.Last().ToLowerInvariant() == ".headers")
        {
            var content = Encoding.UTF8.GetString(twrite.Data.ToArray());
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2) _headers[parts[0].Trim()] = parts[1].Trim();
            }
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }

        if (_currentPath.Count >= 2 && _currentPath[0] == "actions")
        {
            var action = _currentPath.Last();
            
            // Hardening: decode body via pinned char array
            var bodyBytes = twrite.Data.Span;
            char[] bodyChars = GC.AllocateArray<char>(Encoding.UTF8.GetCharCount(bodyBytes), pinned: true);
            try {
                Encoding.UTF8.GetChars(bodyBytes, bodyChars);
                string xml = new string(bodyChars);
                
                try {
                    var responseXml = await _transport.CallActionAsync(action, xml, _headers);
                    
                    if (_lastResponseBytes != null) Array.Clear(_lastResponseBytes);
                    _lastResponseBytes = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(responseXml), pinned: true);
                    Encoding.UTF8.GetBytes(responseXml, 0, responseXml.Length, _lastResponseBytes, 0);
                    
                    return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
                }
                catch (Exception ex) {
                    throw new NinePProtocolException($"SOAP call failed: {ex.Message}");
                }
            }
            finally {
                Array.Clear(bodyChars);
            }
        }

        throw new NotSupportedException("Invalid SOAP path for write.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        if (_lastResponseBytes != null) {
            Array.Clear(_lastResponseBytes);
            _lastResponseBytes = null;
        }
        return new Rclunk(tclunk.Tag);
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "soap";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var qid = new Qid(QidType.QTDIR, 0, 0);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new SoapFileSystem(_config, _transport, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._headers = new Dictionary<string, string>(_headers);
        clone.DotU = DotU;
        // lastResponse is not cloned as it is ephemeral per open FID
        return clone;
    }
}

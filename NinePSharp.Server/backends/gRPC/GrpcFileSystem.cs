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

namespace NinePSharp.Server.Backends.gRPC;

public class GrpcFileSystem : INinePFileSystem
{
    private readonly GrpcBackendConfig _config;
    private readonly IGrpcTransport _transport;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();
    private byte[]? _lastResponse;
    private Dictionary<string, string> _metadata = new();

    public bool DotU { get; set; }

    public GrpcFileSystem(GrpcBackendConfig config, IGrpcTransport transport, ILuxVaultService vault)
    {
        _config = config;
        _transport = transport;
        _vault = vault;
    }

    private bool IsDirectory(List<string> path)
    {
        if (path.Count == 0) return true;
        if (path[0].ToLowerInvariant() == "services")
        {
            if (path.Count == 1 || path.Count == 2) return true; // services/ or services/Service/
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
            if (_lastResponse != null) {
                Array.Clear(_lastResponse);
                _lastResponse = null;
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
                files.Add(("services", QidType.QTDIR));
                files.Add(("status", QidType.QTFILE));
                files.Add((".metadata", QidType.QTFILE));
            }
            else if (_currentPath.Count == 1 && _currentPath[0].ToLowerInvariant() == "services")
            {
                files.Add(("Greeter", QidType.QTDIR));
            }
            else if (_currentPath.Count == 2 && _currentPath[0].ToLowerInvariant() == "services")
            {
                files.Add(("SayHello", QidType.QTFILE));
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
            dataToRead = entries.ToArray();

            int totalToSend = 0;
            int currentOffset = (int)tread.Offset;
            while (currentOffset + 2 <= dataToRead.Length)
            {
                ushort entrySize = (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dataToRead.AsSpan(currentOffset, 2)) + 2);
                if (totalToSend + entrySize > tread.Count) break;
                totalToSend += entrySize;
                currentOffset += entrySize;
            }
            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, dataToRead.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }
        else if (tread.Offset == 0)
        {
            if (_currentPath.Count == 1 && _currentPath[0] == ".metadata")
            {
                var sb = new StringBuilder();
                foreach (var m in _metadata) sb.AppendLine($"{m.Key}: {m.Value}");
                dataToRead = Encoding.UTF8.GetBytes(sb.ToString());
            }
            else if (_currentPath.Count == 1 && _currentPath[0] == "status")
            {
                dataToRead = Encoding.UTF8.GetBytes($"gRPC Backend: {_config.Host}:{_config.Port}\n");
            }
            else if (!IsDirectory(_currentPath))
            {
                dataToRead = _lastResponse;
            }
        }
        else if (!IsDirectory(_currentPath))
        {
            dataToRead = _lastResponse;
        }

        if (dataToRead == null || tread.Offset >= (ulong)dataToRead.Length)
            return new Rread(tread.Tag, Array.Empty<byte>());
        
        var chunk = dataToRead.AsSpan((int)tread.Offset, (int)Math.Min((long)tread.Count, (long)dataToRead.Length - (long)tread.Offset)).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath)) throw new NinePProtocolException("Cannot write to directory.");

        if (_currentPath.Count > 0 && _currentPath.Last().ToLowerInvariant() == ".metadata")
        {
            var content = Encoding.UTF8.GetString(twrite.Data.ToArray());
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2) _metadata[parts[0].Trim()] = parts[1].Trim();
            }
            return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
        }

        if (_currentPath.Count >= 3 && _currentPath[0] == "services")
        {
            var service = _currentPath[1];
            var method = _currentPath[2];
            
            // Hardening: use pinned buffer for payload
            var payloadBytes = twrite.Data.ToArray();
            byte[] pinnedPayload = GC.AllocateArray<byte>(payloadBytes.Length, pinned: true);
            payloadBytes.CopyTo(pinnedPayload, 0);
            Array.Clear(payloadBytes);

            try {
                var response = await _transport.CallAsync(service, method, pinnedPayload, _metadata);
                
                if (_lastResponse != null) Array.Clear(_lastResponse);
                _lastResponse = GC.AllocateArray<byte>(response.Length, pinned: true);
                response.CopyTo(_lastResponse, 0);
                // In a real stub, response might be the same array, but we assume it's fresh or we've copied it.
                
                return new Rwrite(twrite.Tag, (uint)twrite.Data.Length);
            }
            catch (Exception ex) {
                throw new NinePProtocolException($"gRPC call failed: {ex.Message}");
            }
            finally {
                Array.Clear(pinnedPayload);
            }
        }

        throw new NotSupportedException("Invalid gRPC path for write.");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        if (_lastResponse != null) {
            Array.Clear(_lastResponse);
            _lastResponse = null;
        }
        return new Rclunk(tclunk.Tag);
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.LastOrDefault() ?? "grpc";
        bool isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, GetQid(_currentPath), (uint)NinePConstants.FileMode9P.DMDIR | 0755, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new GrpcFileSystem(_config, _transport, _vault);
        clone._currentPath = new List<string>(_currentPath);
        clone._metadata = new Dictionary<string, string>(_metadata);
        clone._lastResponse = null; // Ensure response state is NOT shared
        return clone;
    }
}

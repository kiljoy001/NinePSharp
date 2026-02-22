using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class DatabaseFileSystem : INinePFileSystem
{
    private readonly DatabaseBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private readonly SecureString? _credentials;
    private List<string> _currentPath = new();

    public bool DotU { get; set; }

    public DatabaseFileSystem(DatabaseBackendConfig config, ILuxVaultService vault, SecureString? credentials = null)
    {
        _config = config;
        _vault = vault;
        _credentials = credentials;
    }

    private IDbConnection CreateConnection()
    {
        try 
        {
            var factory = DbProviderFactories.GetFactory(_config.ProviderName);
            var connection = factory.CreateConnection();
            if (connection == null) throw new InvalidOperationException($"Could not create connection for provider {_config.ProviderName}");
            connection.ConnectionString = _config.ConnectionString;
            return connection;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Database FS] Error creating connection: {ex.Message}");
            throw;
        }
    }

    private bool IsDirectory(List<string> path)
    {
        return true;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        var qids = new List<Qid>();
        var tempPath = new List<string>(_currentPath);

        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (tempPath.Count > 0) tempPath.RemoveAt(tempPath.Count - 1);
            }
            else
            {
                tempPath.Add(name);
            }
            
            var type = IsDirectory(tempPath) ? QidType.QTDIR : QidType.QTFILE;
            qids.Add(new Qid(type, 0, (ulong)name.GetHashCode()));
        }
        
        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        var type = IsDirectory(_currentPath) ? QidType.QTDIR : QidType.QTFILE;
        return new Ropen(topen.Tag, new Qid(type, 0, 0), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_currentPath.Count == 0)
        {
            var tables = new[] { "Users", "Products", "Orders" };
            var entries = new List<byte>();
            foreach (var table in tables)
            {
                var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, (ulong)table.GetHashCode()), 
                                 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, 
                                 table, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer);
            }

            var allData = entries.ToArray();
            if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());

            int totalToSend = 0;
            int currentOffset = (int)tread.Offset;
            while (currentOffset + 2 <= allData.Length)
            {
                int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2;
                if (entrySize <= 0 || currentOffset + entrySize > allData.Length) break;
                if (totalToSend + entrySize > tread.Count) break;
                totalToSend += entrySize;
                currentOffset += entrySize;
            }

            if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
            return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "database";
        var isDir = IsDirectory(_currentPath);
        var stat = new Stat(0, 0, 0, new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, 0), 0755 | (isDir ? (uint)NinePConstants.FileMode9P.DMDIR : 0), 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new DatabaseFileSystem(_config, _vault, _credentials);
        clone._currentPath = new List<string>(_currentPath);
        clone.DotU = this.DotU;
        return clone;
    }
}

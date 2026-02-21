using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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

    public DatabaseFileSystem(DatabaseBackendConfig config, ILuxVaultService vault)
    {
        _config = config;
        _vault = vault;
    }

    private IDbConnection CreateConnection()
    {
        try 
        {
            // Use DbProviderFactories to remain provider-agnostic as requested.
            // The user must ensure the provider (e.g. Microsoft.Data.Sqlite) is registered.
            var factory = DbProviderFactories.GetFactory(_config.ProviderName);
            var connection = factory.CreateConnection();
            if (connection == null) throw new InvalidOperationException($"Could not create connection for provider {_config.ProviderName}");
            connection.ConnectionString = _config.ConnectionString;
            return connection;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Database FS] Error creating connection: {ex.Message}. Falling back to mock behavior for demonstration.");
            // Returning null or throwing here. For now, I'll throw as it's a configuration error.
            throw;
        }
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        Console.WriteLine($"[Database FS] Walk: Path={string.Join("/", twalk.Wname)}");
        
        if (twalk.Wname.Length == 0) // Walking to self
        {
            return new Rwalk(twalk.Tag, Array.Empty<Qid>());
        }

        // Conceptually: check if Wname[0] is a valid table name in the database
        // For now, we'll just accept anything as a directory walk
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        // Root or table is a directory
        return new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset == 0)
        {
            // Conceptually: 
            // 1. Open connection
            // 2. Query schema for table names
            // 3. Return as directory entries
            Console.WriteLine($"[Database FS] Listing tables using provider: {_config.ProviderName}");
            
            // Mocking table list for now to avoid crashing without a real DB installed
            var tables = new[] { "Users", "Products", "Orders" };
            var entries = new List<byte>();
            foreach (var table in tables)
            {
                var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, (ulong)table.GetHashCode()), 
                                 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, 
                                 table, "scott", "scott", "scott");
                
                var entryBuffer = new byte[stat.Size + 2];
                int offset = 0;
                stat.WriteTo(entryBuffer, ref offset);
                entries.AddRange(entryBuffer.Take(offset));
            }
            return new Rread(tread.Tag, entries.ToArray());
        }

        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, "database", "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        return new DatabaseFileSystem(_config, _vault);
    }
}

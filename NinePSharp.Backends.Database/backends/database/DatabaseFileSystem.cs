using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Backends;

/// <summary>
/// Provides a 9P filesystem interface to relational and NoSQL databases.
/// Exposes tables as files and allows executing ad-hoc queries by writing to a virtual query file.
/// </summary>
public class DatabaseFileSystem : INinePFileSystem
{
    private readonly DatabaseBackendConfig _config;
    private readonly ILuxVaultService _vault;
    private readonly SecureString? _credentials;
    private readonly IDatabaseQueryExecutor _queryExecutor;
    private readonly Dictionary<string, DatabaseQueryConfig> _configuredQueries;
    private readonly Dictionary<string, string> _queryOverrides = new(StringComparer.Ordinal);

    private List<string> _currentPath = new();
    private string? _adHocQuery;
    private string? _cachedFileName;
    private byte[]? _cachedContent;

    public bool DotU { get; set; }

    public DatabaseFileSystem(DatabaseBackendConfig config, ILuxVaultService vault, SecureString? credentials = null)
        : this(config, vault, credentials, queryExecutor: null, noSqlHandler: null)
    {
    }

    internal DatabaseFileSystem(
        DatabaseBackendConfig config,
        ILuxVaultService vault,
        SecureString? credentials,
        IDatabaseQueryExecutor? queryExecutor,
        HttpMessageHandler? noSqlHandler = null)
    {
        _config = config;
        _vault = vault;
        _credentials = credentials;

        _configuredQueries = (_config.Queries ?? new List<DatabaseQueryConfig>())
            .Where(q => !string.IsNullOrWhiteSpace(q.Name))
            .GroupBy(q => q.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToDictionary(q => q.Name, q => q, StringComparer.Ordinal);

        var (username, password) = ParseCredentials(credentials);
        _queryExecutor = queryExecutor ?? CreateExecutor(username, password, noSqlHandler);
    }

    private static (string Username, string Password) ParseCredentials(SecureString? credentials)
    {
        if (credentials == null)
        {
            return (string.Empty, string.Empty);
        }

        string raw = SecureStringHelper.ToString(credentials);
        string[] parts = raw.Split(':', 2);
        string user = parts.Length > 0 ? parts[0] : string.Empty;
        string pass = parts.Length > 1 ? parts[1] : string.Empty;
        return (user, pass);
    }

    private IDatabaseQueryExecutor CreateExecutor(string username, string password, HttpMessageHandler? noSqlHandler)
    {
        if (_config.NoSql != null && !string.IsNullOrWhiteSpace(_config.NoSql.EndpointUrl))
        {
            return new NoSqlHttpQueryExecutor(_config, username, password, noSqlHandler);
        }

        return new AdoNetQueryExecutor(_config, username, password);
    }

    private static Qid GetQid(IReadOnlyList<string> path)
    {
        bool isDir = path.Count == 0 || (path.Count == 1 && path[0] == "tables");
        string key = path.Count == 0 ? "/" : string.Join("/", path);
        return new Qid(isDir ? QidType.QTDIR : QidType.QTFILE, 0, DeterministicHash.GetStableHash64(key));
    }

    private bool IsDirectory(IReadOnlyList<string> path) => path.Count == 0 || (path.Count == 1 && path[0] == "tables");

    private bool IsKnownFile(string name)
    {
        if (name == "status") return true;
        if (_config.AllowAdHocQuery && name == "query") return true;
        return _configuredQueries.ContainsKey(name);
    }

    private bool IsWritableFile(string name)
    {
        if (_config.AllowAdHocQuery && name == "query") return true;
        return _configuredQueries.TryGetValue(name, out var query) && query.Writable;
    }

    private void InvalidateCache()
    {
        _cachedFileName = null;
        _cachedContent = null;
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
                if (tempPath.Count > 0)
                {
                    tempPath.RemoveAt(tempPath.Count - 1);
                }

                qids.Add(GetQid(tempPath));
                continue;
            }

            if (!IsDirectory(tempPath))
            {
                if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                break;
            }

            if (tempPath.Count == 0) {
                if (!IsKnownFile(name) && name != "tables") {
                    if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                    break;
                }
            } else if (tempPath.Count == 1 && tempPath[0] == "tables") {
                // We allow walking into any name under tables/, 
                // but validation happens during Read if it doesn't exist.
            } else {
                 if (qids.Count == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
                 break;
            }

            tempPath.Add(name);
            qids.Add(GetQid(tempPath));
        }

        if (qids.Count == twalk.Wname.Length)
        {
            _currentPath = tempPath;
            InvalidateCache();
        }

        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public Task<Ropen> OpenAsync(Topen topen)
    {
        return Task.FromResult(new Ropen(topen.Tag, GetQid(_currentPath), 0));
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (IsDirectory(_currentPath))
        {
            byte[] listing;
            if (_currentPath.Count == 1 && _currentPath[0] == "tables") {
                listing = await BuildTablesListingAsync();
            } else {
                listing = BuildRootListing();
            }
            return SliceDirectoryRead(tread, listing);
        }

        string fileName = _currentPath.Last();
        bool isTable = _currentPath.Count == 2 && _currentPath[0] == "tables";
        string cacheKey = isTable ? $"table:{fileName}" : fileName;
        
        bool refresh = tread.Offset == 0 || !string.Equals(_cachedFileName, cacheKey, StringComparison.Ordinal) || _cachedContent == null;
        byte[] payload = await GetFilePayloadAsync(fileName, refresh, isTable);

        if (tread.Offset >= (ulong)payload.Length)
        {
            return new Rread(tread.Tag, Array.Empty<byte>());
        }

        int count = (int)Math.Min((long)tread.Count, payload.Length - (long)tread.Offset);
        byte[] chunk = payload.AsSpan((int)tread.Offset, count).ToArray();
        return new Rread(tread.Tag, chunk);
    }

    private async Task<byte[]> GetFilePayloadAsync(string fileName, bool refresh, bool isTable = false)
    {
        string cacheKey = isTable ? $"table:{fileName}" : fileName;
        if (!refresh && _cachedContent != null && string.Equals(_cachedFileName, cacheKey, StringComparison.Ordinal))
        {
            return _cachedContent;
        }

        string content;
        if (isTable) {
             var tables = await _queryExecutor.GetTablesAsync();
             if (!tables.Contains(fileName, StringComparer.OrdinalIgnoreCase))
             {
                 throw new NinePProtocolException($"Table '{fileName}' not found.");
             }
             // Use brackets for SQL Server/SQLite, but even better to be safe
             content = await ExecuteQueryAsync($"SELECT * FROM \"{fileName.Replace("\"", "\"\"")}\"");
        }
        else if (fileName == "status")
        {
            content = $"Database backend ({_queryExecutor.Engine})\nConfigured queries: {_configuredQueries.Count}\nAd-hoc query: {(_config.AllowAdHocQuery ? "enabled" : "disabled")}\n";
        }
        else if (_config.AllowAdHocQuery && fileName == "query")
        {
            if (string.IsNullOrWhiteSpace(_adHocQuery))
            {
                content = "No ad-hoc query is set. Write query text to this file first.\n";
            }
            else
            {
                content = await ExecuteQueryAsync(_adHocQuery);
            }
        }
        else if (_configuredQueries.TryGetValue(fileName, out var queryConfig))
        {
            string query = _queryOverrides.TryGetValue(fileName, out var overrideQuery)
                ? overrideQuery
                : queryConfig.Query;

            if (string.IsNullOrWhiteSpace(query))
            {
                content = "Configured query is empty.\n";
            }
            else
            {
                content = await ExecuteQueryAsync(query);
            }
        }
        else
        {
            content = "Not found.\n";
        }

        if (!content.EndsWith('\n'))
        {
            content += "\n";
        }

        _cachedFileName = fileName;
        _cachedContent = Encoding.UTF8.GetBytes(content);
        return _cachedContent;
    }

    private async Task<string> ExecuteQueryAsync(string query)
    {
        try
        {
            return await _queryExecutor.ExecuteAsync(query);
        }
        catch (Exception ex)
        {
            throw new NinePProtocolException($"Database query failed: {ex.Message}");
        }
    }

    private async Task<byte[]> BuildTablesListingAsync()
    {
        var tables = await _queryExecutor.GetTablesAsync();
        var data = new List<byte>();
        foreach (var table in tables.OrderBy(t => t))
        {
            var stat = new Stat(
                0, 0, 0,
                new Qid(QidType.QTFILE, 0, (ulong)($"tables/{table}").GetHashCode()),
                0444, 0, 0, 0, table, "scott", "scott", "scott", dotu: DotU);

            var buffer = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(buffer, ref offset);
            data.AddRange(buffer);
        }
        return data.ToArray();
    }

    private byte[] BuildRootListing()
    {
        var entries = new List<(string Name, bool Writable, bool IsDir)>();
        foreach (var query in _configuredQueries.Values.OrderBy(q => q.Name, StringComparer.Ordinal))
        {
            entries.Add((query.Name, query.Writable, false));
        }

        if (_config.AllowAdHocQuery)
        {
            entries.Add(("query", true, false));
        }

        entries.Add(("tables", false, true));
        entries.Add(("status", false, false));

        var data = new List<byte>();
        foreach (var entry in entries)
        {
            uint mode = entry.Writable ? 0x1B6u : 0x124u; // 0666 or 0444
            if (entry.IsDir) mode = (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu;
            
            var stat = new Stat(
                0,
                0,
                0,
                new Qid(entry.IsDir ? QidType.QTDIR : QidType.QTFILE, 0, (ulong)entry.Name.GetHashCode()),
                mode,
                0,
                0,
                0,
                entry.Name,
                "scott",
                "scott",
                "scott",
                dotu: DotU);

            var buffer = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(buffer, ref offset);
            data.AddRange(buffer);
        }

        return data.ToArray();
    }

    private static Rread SliceDirectoryRead(Tread tread, byte[] allData)
    {
        if (tread.Offset >= (ulong)allData.Length)
        {
            return new Rread(tread.Tag, Array.Empty<byte>());
        }

        int totalToSend = 0;
        int currentOffset = (int)tread.Offset;
        while (currentOffset + 2 <= allData.Length)
        {
            int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2;
            if (entrySize <= 0 || currentOffset + entrySize > allData.Length)
            {
                break;
            }

            if (totalToSend + entrySize > tread.Count)
            {
                break;
            }

            totalToSend += entrySize;
            currentOffset += entrySize;
        }

        if (totalToSend == 0)
        {
            return new Rread(tread.Tag, Array.Empty<byte>());
        }

        return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
    {
        if (IsDirectory(_currentPath))
        {
            throw new NinePProtocolException("Cannot write to directory.");
        }

        string fileName = _currentPath[0];
        string queryText = Encoding.UTF8.GetString(twrite.Data.Span).Trim();

        if (_config.AllowAdHocQuery && fileName == "query")
        {
            _adHocQuery = queryText;
            InvalidateCache();
            return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
        }

        if (_configuredQueries.TryGetValue(fileName, out var queryConfig) && queryConfig.Writable)
        {
            _queryOverrides[fileName] = queryText;
            InvalidateCache();
            return Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));
        }

        throw new NinePProtocolException("Target file is read-only.");
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        bool isDir = IsDirectory(_currentPath);
        string name = isDir ? "database" : _currentPath[0];
        uint mode = isDir
            ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu
            : (IsWritableFile(name) ? 0x1B6u : 0x124u);

        var stat = new Stat(
            0,
            0,
            0,
            GetQid(_currentPath),
            mode,
            0,
            0,
            0,
            name,
            "scott",
            "scott",
            "scott",
            dotu: DotU);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException("Database backend is read-only.");
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException("Database backend is read-only.");

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        bool isDir = IsDirectory(_currentPath);
        string name = isDir ? "database" : _currentPath.Last();
        uint mode = isDir
            ? (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu
            : (IsWritableFile(name) ? 0x1B6u : 0x124u);

        var qid = GetQid(_currentPath);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, mode);
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => throw new NotSupportedException("Database backend is read-only.");

    public INinePFileSystem Clone()
    {
        var clone = new DatabaseFileSystem(_config, _vault, _credentials, _queryExecutor);
        clone._currentPath = new List<string>(_currentPath);
        clone._adHocQuery = _adHocQuery;
        clone.DotU = DotU;

        foreach (var kvp in _queryOverrides)
        {
            clone._queryOverrides[kvp.Key] = kvp.Value;
        }

        if (_cachedContent != null)
        {
            clone._cachedFileName = _cachedFileName;
            clone._cachedContent = _cachedContent.ToArray();
        }

        return clone;
    }
}

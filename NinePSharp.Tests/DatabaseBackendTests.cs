using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class DatabaseBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();

    private sealed class FakeQueryExecutor : IDatabaseQueryExecutor
    {
        private readonly Func<string, string> _handler;
        public string Engine => "fake:test";
        public List<string> ExecutedQueries { get; } = new();

        public FakeQueryExecutor(Func<string, string> handler)
        {
            _handler = handler;
        }

        public Task<string> ExecuteAsync(string query)
        {
            ExecutedQueries.Add(query);
            return Task.FromResult(_handler(query));
        }

        public Task<IEnumerable<string>> GetTablesAsync()
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }
    }

    [Fact]
    public async Task Database_RootListing_Exposes_ConfiguredQueries_And_SystemFiles()
    {
        var config = new DatabaseBackendConfig
        {
            MountPath = "/db",
            ProviderName = "MockProvider",
            ConnectionString = "Data Source=:memory:",
            AllowAdHocQuery = true,
            Queries = new List<DatabaseQueryConfig>
            {
                new() { Name = "users.json", Query = "select * from users" },
                new() { Name = "products.json", Query = "select * from products" }
            }
        };

        var fs = new DatabaseFileSystem(config, _vaultMock.Object, null, new FakeQueryExecutor(_ => "[]"));

        var readRoot = await fs.ReadAsync(new Tread(1, 0, 0, 8192));
        var entries = ParseDirectory(readRoot.Data.ToArray()).Select(s => s.Name).ToArray();

        Assert.Contains("users.json", entries);
        Assert.Contains("products.json", entries);
        Assert.Contains("query", entries);
        Assert.Contains("status", entries);
    }

    [Fact]
    public async Task Database_Read_ConfiguredQuery_UsesQueryExecutor()
    {
        const string sql = "select * from users";
        var config = new DatabaseBackendConfig
        {
            MountPath = "/db",
            ProviderName = "MockProvider",
            ConnectionString = "Data Source=:memory:",
            Queries = new List<DatabaseQueryConfig>
            {
                new() { Name = "users.json", Query = sql }
            }
        };

        var executor = new FakeQueryExecutor(query => query == sql ? "[{\"id\":1}]" : "[]");
        var fs = new DatabaseFileSystem(config, _vaultMock.Object, null, executor);

        var walk = await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "users.json" }));
        Assert.Single(walk.Wqid);

        var read = await fs.ReadAsync(new Tread(2, 1, 0, 4096));
        var payload = Encoding.UTF8.GetString(read.Data.ToArray());

        Assert.Contains("\"id\":1", payload);
        Assert.Contains(sql, executor.ExecutedQueries);
    }

    [Fact]
    public async Task Database_AdHocQuery_WriteThenRead_ExecutesWrittenQuery()
    {
        var config = new DatabaseBackendConfig
        {
            MountPath = "/db",
            ProviderName = "MockProvider",
            ConnectionString = "Data Source=:memory:",
            AllowAdHocQuery = true
        };

        var executor = new FakeQueryExecutor(query => $"{{\"query\":\"{query}\"}}");
        var fs = new DatabaseFileSystem(config, _vaultMock.Object, null, executor);

        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "query" }));

        const string dynamicQuery = "select count(*) from users";
        await fs.WriteAsync(new Twrite(2, 1, 0, Encoding.UTF8.GetBytes(dynamicQuery)));

        var read = await fs.ReadAsync(new Tread(3, 1, 0, 4096));
        var payload = Encoding.UTF8.GetString(read.Data.ToArray());

        Assert.Contains(dynamicQuery, payload);
        Assert.Contains(dynamicQuery, executor.ExecutedQueries);
    }

    [Fact]
    public async Task Database_WritableConfiguredQuery_CanBeOverriddenByWrite()
    {
        const string original = "select * from products";
        const string overrideQuery = "select * from products where id = 42";

        var config = new DatabaseBackendConfig
        {
            MountPath = "/db",
            ProviderName = "MockProvider",
            ConnectionString = "Data Source=:memory:",
            Queries = new List<DatabaseQueryConfig>
            {
                new() { Name = "products.json", Query = original, Writable = true }
            }
        };

        var executor = new FakeQueryExecutor(query => $"{{\"executed\":\"{query}\"}}");
        var fs = new DatabaseFileSystem(config, _vaultMock.Object, null, executor);

        await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "products.json" }));
        await fs.WriteAsync(new Twrite(2, 1, 0, Encoding.UTF8.GetBytes(overrideQuery)));

        var read = await fs.ReadAsync(new Tread(3, 1, 0, 4096));
        string payload = Encoding.UTF8.GetString(read.Data.ToArray());

        Assert.Contains(overrideQuery, payload);
        Assert.Equal(overrideQuery, executor.ExecutedQueries.Last());
    }

    private static List<Stat> ParseDirectory(byte[] data)
    {
        var stats = new List<Stat>();
        int offset = 0;
        while (offset < data.Length)
        {
            stats.Add(new Stat(data, ref offset));
        }

        return stats;
    }
}

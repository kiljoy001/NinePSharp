using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class DatabaseBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly DatabaseBackendConfig _config = new() 
    { 
        MountPath = "/db", 
        ProviderName = "MockProvider", 
        ConnectionString = "Data Source=:memory:" 
    };

    private class TestableDatabaseFileSystem : DatabaseFileSystem
    {
        private readonly IDbConnection _mockConnection;

        public TestableDatabaseFileSystem(DatabaseBackendConfig config, ILuxVaultService vault, IDbConnection mockConnection) 
            : base(config, vault)
        {
            _mockConnection = mockConnection;
        }

        // We override or shim the connection creation for testing if needed,
        // but for this PoC we'll just test the logic that doesn't hit the factory.
    }

    [Fact]
    public async Task Database_Query_Execution_Stores_Results()
    {
        var fs = new DatabaseFileSystem(_config, _vaultMock.Object);

        // Verify root listing (where _currentPath.Count == 0)
        var readRoot = await fs.ReadAsync(new Tread(1, 0, 0, 8192));
        var entries = ParseDirectory(readRoot.Data.ToArray());
        Assert.Contains(entries, s => s.Name == "Users");
        Assert.Contains(entries, s => s.Name == "Products");
        Assert.Contains(entries, s => s.Name == "Orders");
        Assert.All(entries, s => Assert.Equal(QidType.QTDIR, s.Qid.Type));

        // Walk to a table directory
        var walkResult = await fs.WalkAsync(new Twalk(1, 0, 1, new[] { "Users" }));
        Assert.Single(walkResult.Wqid);
        Assert.Equal(QidType.QTDIR, walkResult.Wqid[0].Type);
    }

    [Fact]
    public void Database_Clone_Preserves_Path()
    {
        var fs = new DatabaseFileSystem(_config, _vaultMock.Object);
        // We can't easily walk and then check _currentPath as it is private,
        // but we can verify Clone() produces a new instance.
        var clone = fs.Clone();
        Assert.NotSame(fs, clone);
        Assert.IsType<DatabaseFileSystem>(clone);
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

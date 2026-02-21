using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Constants;
using Xunit;
using FluentAssertions;
using System.Threading.Tasks;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests;

public class DatabaseBackendTests
{
    private readonly ILuxVaultService _vault = new LuxVaultService();

    [Fact]
    public async Task DatabaseFileSystem_StatAsync_Returns_Correct_Mode()
    {
        // Arrange
        var config = new DatabaseBackendConfig 
        { 
            MountPath = "/inventory",
            ConnectionString = "Data Source=test.db",
            ProviderName = "Microsoft.Data.Sqlite"
        };
        var fs = new DatabaseFileSystem(config, _vault);
        var tstat = new Tstat(1, 100);

        // Act
        var response = await fs.StatAsync(tstat);

        // Assert
        response.Should().BeOfType<Rstat>();
        var rstat = (Rstat)response;
        rstat.Stat.Name.Should().Be("database");
        (rstat.Stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR).Should().NotBe(0);
    }
}

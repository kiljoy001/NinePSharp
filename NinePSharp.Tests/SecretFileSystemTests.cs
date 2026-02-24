using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

public class SecretFileSystemTests
{
    public SecretFileSystemTests()
    {
        SecretFileSystem.ClearSessionPasswords();
    }

    private static readonly ILuxVaultService _vault = new LuxVaultService();
    private static readonly SecretBackendConfig _config = new SecretBackendConfig();

    [Fact]
    public async Task SecretFileSystem_StatfsAsync_Returns_Valid_Stats()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert
        result.Should().BeOfType<Rstatfs>();
        result.Tag.Should().Be(1);
        result.BSize.Should().Be(4096);
        result.FsType.Should().Be(0x01021997);
        result.Files.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SecretFileSystem_StatfsAsync_Root_Has_3_Files()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert
        result.Files.Should().Be(4); // 3 files (provision, unlock, vault) + 1 directory
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Root_Lists_Expected_Entries()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act
        var result = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        result.Count.Should().BeGreaterThan(0);

        // Parse the entries to verify content
        var dataString = Encoding.UTF8.GetString(result.Data.Span);
        dataString.Should().Contain("provision");
        dataString.Should().Contain("unlock");
        dataString.Should().Contain("vault");
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_VaultDir_Is_Empty_Initially()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Walk to vault directory
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" }));

        // Act
        var result = await fs.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192));

        // Assert
        result.Should().BeOfType<Rreaddir>();
        // Vault directory should be empty initially (no unlocked secrets)
        result.Data.Length.Should().Be(0);
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Fails_If_Not_Directory()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Walk to a file (provision)
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "provision" }));

        // Act & Assert
        Func<Task> act = async () => await fs.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192));
        await act.Should().ThrowAsync<NinePProtocolException>()
            .WithMessage("*Not a directory*");
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Supports_Pagination_With_Offset()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act - Read first batch
        var result1 = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));
        result1.Count.Should().BeGreaterThan(0);

        // Act - Read with offset (skip some entries)
        var result2 = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 50, 8192));

        // Assert - Second read should have fewer or equal entries
        result2.Count.Should().BeLessThanOrEqualTo(result1.Count);
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Respects_Count_Limit()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act - Read with very small count limit
        var result = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 50));

        // Assert - Should return limited data
        result.Count.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task SecretFileSystem_StatfsAsync_After_Walk_To_Vault()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Walk to vault directory
        await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" }));

        // Act
        var result = await fs.StatfsAsync(new Tstatfs(100, 2, 2));

        // Assert
        result.Should().BeOfType<Rstatfs>();
        result.Files.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Returns_Empty_For_Large_Offset()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act - Request with offset beyond all entries
        var result = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 10000, 8192));

        // Assert - Should return empty
        result.Data.Length.Should().Be(0);
    }

    [Fact]
    public async Task SecretFileSystem_Clone_Preserves_Current_Path()
    {
        // Arrange
        var fs1 = new SecretFileSystem(NullLogger.Instance, _config, _vault);
        await fs1.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" }));

        // Act - Clone
        var fs2 = (SecretFileSystem)fs1.Clone();

        // Act - Read from fs2 (should be in vault directory)
        var result = await fs2.ReaddirAsync(new Treaddir(100, 2, 2, 0, 8192));

        // Assert - Should be able to read vault directory
        result.Should().BeOfType<Rreaddir>();
    }

    [Fact]
    public async Task SecretFileSystem_Clone_Creates_Independent_Instance()
    {
        // Arrange
        var fs1 = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act - Clone and modify fs1
        var fs2 = (SecretFileSystem)fs1.Clone();
        await fs1.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" }));

        // Act - Read from fs2 (should still be at root)
        var result = await fs2.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));
        var dataString = Encoding.UTF8.GetString(result.Data.Span);

        // Assert - fs2 should still be at root and see root files
        dataString.Should().Contain("provision");
        dataString.Should().Contain("vault");
    }

    [Fact]
    public async Task SecretFileSystem_StatfsAsync_Returns_Consistent_Values()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act - Call multiple times
        var result1 = await fs.StatfsAsync(new Tstatfs(100, 1, 1));
        var result2 = await fs.StatfsAsync(new Tstatfs(100, 1, 1));

        // Assert - Should return consistent values
        result1.BSize.Should().Be(result2.BSize);
        result1.FsType.Should().Be(result2.FsType);
        result1.Files.Should().Be(result2.Files);
    }

    [Fact]
    public async Task SecretFileSystem_ReaddirAsync_Returns_Valid_Qid_Types()
    {
        // Arrange
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Act
        var result = await fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192));

        // Assert
        result.Data.Length.Should().BeGreaterThan(0);

        // Parse first entry and verify QID structure
        var data = result.Data.Span;
        if (data.Length >= 13)
        {
            var qidType = (QidType)data[0];
            qidType.Should().BeOneOf(QidType.QTFILE, QidType.QTDIR);
        }
    }
}

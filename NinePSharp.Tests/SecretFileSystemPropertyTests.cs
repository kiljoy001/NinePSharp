using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

public class SecretFileSystemPropertyTests
{
    public SecretFileSystemPropertyTests()
    {
        SecretFileSystem.ClearSessionPasswords();
    }

    private static readonly ILuxVaultService _vault = new LuxVaultService();
    private static readonly SecretBackendConfig _config = new SecretBackendConfig();

    [Property(MaxTest = 30)]
    public void SecretFileSystem_StatfsAsync_BSize_Is_Constant(bool walkToVault)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        if (walkToVault)
        {
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();
        }

        var statfs = fs.StatfsAsync(new Tstatfs(100, walkToVault ? (ushort)2 : (ushort)1, walkToVault ? (ushort)2 : (ushort)1)).Result;

        // BSize should always be 4096
        statfs.BSize.Should().Be(4096);
    }

    [Property(MaxTest = 30)]
    public void SecretFileSystem_StatfsAsync_FsType_Is_Constant(bool walkToVault)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        if (walkToVault)
        {
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();
        }

        var statfs = fs.StatfsAsync(new Tstatfs(100, walkToVault ? (ushort)2 : (ushort)1, walkToVault ? (ushort)2 : (ushort)1)).Result;

        // FsType should always be the 9P magic number
        statfs.FsType.Should().Be(0x01021997);
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_Clone_Preserves_Path_Independence(bool walkInOriginal)
    {
        var fs1 = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        if (walkInOriginal)
        {
            fs1.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();
        }

        // Clone
        var fs2 = (SecretFileSystem)fs1.Clone();

        // Walk in clone
        fs2.WalkAsync(new Twalk(1, 1, 3, new[] { "provision" })).Wait();

        // Original should still be able to stat at its current path
        var statfs1 = fs1.StatfsAsync(new Tstatfs(100, walkInOriginal ? (ushort)2 : (ushort)1, walkInOriginal ? (ushort)2 : (ushort)1)).Result;

        // Should succeed without throwing
        statfs1.BSize.Should().Be(4096);
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_ReaddirAsync_Offset_Returns_LessOrEqual_Data(ushort offset)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);
        var safeOffset = (ulong)(offset % 1000);

        // Read all
        var resultAll = fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 8192)).Result;

        // Read with offset
        var resultWithOffset = fs.ReaddirAsync(new Treaddir(100, 1, 1, safeOffset, 8192)).Result;

        // With offset should return less or equal data
        resultWithOffset.Count.Should().BeLessThanOrEqualTo(resultAll.Count);
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_ReaddirAsync_Count_Limit_Is_Respected(PositiveInt countLimit)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);
        var limit = Math.Min((uint)countLimit.Get, 8192);

        var result = fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, limit)).Result;

        // Result count should not exceed limit
        result.Count.Should().BeLessThanOrEqualTo(limit);
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_StatfsAsync_At_Root_Has_Fixed_File_Count()
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        var statfs = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Root should always have 4 files (provision, unlock, vault + root itself)
        statfs.Files.Should().Be(4);
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_Walk_To_Vault_And_Back_Is_Consistent()
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        // Get statfs at root
        var statfsRoot1 = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        // Walk to vault
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();

        // Walk back to root
        fs.WalkAsync(new Twalk(2, 2, 3, new[] { ".." })).Wait();

        // Get statfs at root again
        var statfsRoot2 = fs.StatfsAsync(new Tstatfs(100, 3, 3)).Result;

        // Should be same
        (statfsRoot1.Files == statfsRoot2.Files &&
         statfsRoot1.BSize == statfsRoot2.BSize &&
         statfsRoot1.FsType == statfsRoot2.FsType).Should().BeTrue();
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_Multiple_Clones_Are_Independent(PositiveInt cloneCount)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);
        var count = Math.Min(cloneCount.Get, 5);

        // Create multiple clones and walk them to different places
        for (int i = 0; i < count; i++)
        {
            var clone = (SecretFileSystem)fs.Clone();
            if (i % 2 == 0)
            {
                clone.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();
            }
        }

        // Original should still be at root
        var statfs = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Result;

        statfs.Files.Should().Be(4); // Still at root
    }

    [Property(MaxTest = 20)]
    public void SecretFileSystem_ReaddirAsync_Returns_Valid_Count(bool atVault)
    {
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);

        if (atVault)
        {
            fs.WalkAsync(new Twalk(1, 1, 2, new[] { "vault" })).Wait();
        }

        var result = fs.ReaddirAsync(new Treaddir(100, atVault ? (ushort)2 : (ushort)1, atVault ? (ushort)2 : (ushort)1, 0, 8192)).Result;

        // Count should match data length
        result.Count.Should().Be((uint)result.Data.Length);
    }

    [Fact]
    public async Task SecretFileSystem_Property_Tests_Can_Run_Synchronously()
    {
        // Smoke test
        var fs = new SecretFileSystem(NullLogger.Instance, _config, _vault);
        var statfs = await fs.StatfsAsync(new Tstatfs(100, 1, 1));
        statfs.Files.Should().Be(4); // provision, unlock, vault + root
    }
}

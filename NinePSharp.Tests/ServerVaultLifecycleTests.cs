using System;
using System.IO;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

[Collection("Vault Lifecycle")]
public class ServerVaultLifecycleTests
{
    [Fact]
    public void Startup_Cleans_Vault_Directory()
    {
        string marker = CreateMarkerFile("startup");

        Program.CleanupVaultsOnStartup();

        Assert.False(File.Exists(marker));
        Assert.True(Directory.Exists(LuxVault.VaultDirectory));
    }

    [Fact]
    public void Shutdown_Cleans_Vault_Directory()
    {
        string marker = CreateMarkerFile("shutdown");

        Program.CleanupVaultsOnShutdown();

        Assert.False(File.Exists(marker));
        Assert.True(Directory.Exists(LuxVault.VaultDirectory));
    }

    private static string CreateMarkerFile(string prefix)
    {
        Directory.CreateDirectory(LuxVault.VaultDirectory);
        string marker = Path.Combine(LuxVault.VaultDirectory, $"{prefix}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(marker, "vault-marker");
        return marker;
    }
}

[CollectionDefinition("Vault Lifecycle", DisableParallelization = true)]
public class VaultLifecycleCollectionDefinition
{
}

[CollectionDefinition("Global Arena", DisableParallelization = true)]
public class GlobalArenaCollectionDefinition
{
}

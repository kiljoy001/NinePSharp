using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

[Collection("Sequential Secret Tests")]
public class SecretBackendRegressionTests
{
    static SecretBackendRegressionTests()
    {
        byte[] sessionKey = new byte[32];
        for (int i = 0; i < sessionKey.Length; i++)
        {
            sessionKey[i] = (byte)i;
        }

        LuxVault.InitializeSessionKey(sessionKey);
        ProtectedSecret.InitializeSessionKey(sessionKey);
    }

    [Fact]
    public async Task Unlock_State_Persists_Across_New_FileSystem_Instances()
    {
        var backend = await CreateBackendAsync();
        string secretName = $"reg-unlock-{Guid.NewGuid():N}";
        const string password = "testpass";
        string expected = $"hidden-data-{Guid.NewGuid():N}";

        await ProvisionAsync(backend, password, secretName, expected);
        await UnlockAsync(backend, password, secretName);

        string readBack = await ReadSecretAsync(backend, secretName);
        Assert.Equal(expected, readBack);
    }

    [Fact]
    public async Task GetFileSystem_Returns_PathIsolated_Clone_Per_Connection()
    {
        var backend = await CreateBackendAsync();
        string secretName = $"reg-path-{Guid.NewGuid():N}";
        const string password = "testpass";
        const string expected = "hidden-data";

        await ProvisionAsync(backend, password, secretName, expected);
        await UnlockAsync(backend, password, secretName);

        var fsA = backend.GetFileSystem();
        await WalkToAsync(fsA, "vault", secretName);

        var fsB = backend.GetFileSystem();
        var rootRead = await fsB.ReadAsync(new Tread(1, 1, 0, 8192));
        var rootStats = ParseDirectory(rootRead.Data.ToArray());
        var names = rootStats.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("provision", names);
        Assert.Contains("unlock", names);
        Assert.Contains("vault", names);
    }

    [Fact]
    public async Task Read_Uses_PhysicalVault_Stream_And_Sees_Updated_Secret_After_Unlock()
    {
        var backend = await CreateBackendAsync();
        string secretName = $"reg-stream-{Guid.NewGuid():N}";
        const string password = "testpass";
        const string valueV1 = "hidden-data-v1";
        const string valueV2 = "hidden-data-v2";

        await ProvisionAsync(backend, password, secretName, valueV1);
        await UnlockAsync(backend, password, secretName);

        string firstRead = await ReadSecretAsync(backend, secretName);
        Assert.Equal(valueV1, firstRead);

        // Overwrite the same physical vault entry after unlock.
        await ProvisionAsync(backend, password, secretName, valueV2);

        string secondRead = await ReadSecretAsync(backend, secretName);
        Assert.Equal(valueV2, secondRead);
    }

    [Fact]
    public void StoreSecret_Uses_AppContext_BaseDirectory_Not_CurrentDirectory()
    {
        string secretName = $"reg-cwd-{Guid.NewGuid():N}";
        const string password = "cwd-password";
        byte[] payload = Encoding.UTF8.GetBytes("cwd-data");

        byte[] nameBytes = Encoding.UTF8.GetBytes(secretName);
        byte[] seed = new byte[32];
        LuxVault.DeriveSeed(password, nameBytes, seed);
        string hiddenId = LuxVault.GenerateHiddenId(seed);
        string expectedPath = LuxVault.GetVaultPath($"secret_{hiddenId}.vlt");

        string tempCwd = Path.Combine(Path.GetTempPath(), $"ninepsharp-reg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempCwd);
        string originalCwd = Directory.GetCurrentDirectory();
        string cwdRelativePath = Path.Combine(tempCwd, $"secret_{hiddenId}.vlt");

        using var securePassword = ToSecureString(password);
        try
        {
            Directory.SetCurrentDirectory(tempCwd);
            LuxVault.StoreSecret(secretName, payload, securePassword);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Array.Clear(payload, 0, payload.Length);
            Array.Clear(nameBytes, 0, nameBytes.Length);
            Array.Clear(seed, 0, seed.Length);

            if (Directory.Exists(tempCwd))
            {
                Directory.Delete(tempCwd, recursive: true);
            }
        }

        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(cwdRelativePath));
    }

    private static async Task<SecretBackend> CreateBackendAsync()
    {
        var backend = new SecretBackend(new LuxVaultService(), NullLoggerFactory.Instance);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        await backend.InitializeAsync(config);
        return backend;
    }

    private static async Task ProvisionAsync(SecretBackend backend, string password, string name, string value)
    {
        var fs = backend.GetFileSystem();
        await WalkToAsync(fs, "provision");

        byte[] data = Encoding.UTF8.GetBytes($"{password}:{name}:{value}");
        try
        {
            var write = await fs.WriteAsync(new Twrite(1, 1, 0, data));
            Assert.Equal((uint)data.Length, write.Count);
        }
        finally
        {
            Array.Clear(data);
        }
    }

    private static async Task UnlockAsync(SecretBackend backend, string password, string name)
    {
        var fs = backend.GetFileSystem();
        await WalkToAsync(fs, "unlock");

        byte[] data = Encoding.UTF8.GetBytes($"{password}:{name}");
        try
        {
            var write = await fs.WriteAsync(new Twrite(1, 1, 0, data));
            Assert.Equal((uint)data.Length, write.Count);
        }
        finally
        {
            Array.Clear(data);
        }
    }

    private static async Task<string> ReadSecretAsync(SecretBackend backend, string name)
    {
        var fs = backend.GetFileSystem();
        await WalkToAsync(fs, "vault", name);

        var read = await fs.ReadAsync(new Tread(1, 1, 0, 8192));
        return Encoding.UTF8.GetString(read.Data.Span);
    }

    private static async Task WalkToAsync(INinePFileSystem fs, params string[] path)
    {
        var walk = await fs.WalkAsync(new Twalk(1, 1, 2, path));
        Assert.NotNull(walk.Wqid);
        Assert.Equal(path.Length, walk.Wqid.Length);
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

    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (char c in value)
        {
            secure.AppendChar(c);
        }

        secure.MakeReadOnly();
        return secure;
    }
}

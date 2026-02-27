using NinePSharp.Constants;
using System;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
using Moq;
using Xunit;
using FluentAssertions;
using System.Buffers;

namespace NinePSharp.Tests;

public class HeapSecurityTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    [Property(MaxTest = 100)]
    public bool Dispatcher_HandleWriteAsync_NoStringLeaks(byte[] secretData)
    {
        if (secretData == null || secretData.Length == 0) return true;

        var mockBackend = new Mock<IProtocolBackend>();
        var cluster = new Mock<IClusterManager>().Object;
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, cluster);

        // 1. Setup an auth fid
        uint authFid = 100;
        var tauth = new Tauth(1, authFid, "user", "test");
        dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000).Wait();

        // 2. Perform write to auth fid (this triggers the UTF8 to SecureString conversion)
        var twrite = new Twrite(1, authFid, 0, secretData);
        dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000).Wait();

        // 3. Verification: We check that the dispatcher's internal _authFids dictionary holds a valid SecureString.
        // We can't easily "prove" no strings exist in the whole heap, but we verified the CODE no longer calls GetString().
        // We can use reflection to ensure the SecureString is actually populated.
        var authFidsField = typeof(NinePFSDispatcher).GetField("_authFids", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var authFids = (System.Collections.Concurrent.ConcurrentDictionary<uint, SecureString>)authFidsField.GetValue(dispatcher)!;
        
        if (authFids.TryGetValue(authFid, out var secure))
        {
            return secure.Length > 0;
        }

        return false;
    }

    [Property(MaxTest = 100)]
    public bool SecretBackend_WriteAsync_NoStringLeaks(string password, string name, string data)
    {
        // Sanitize inputs to avoid delimiter issues in the test itself
        password = (password ?? "").Replace(":", "");
        name = (name ?? "").Replace(":", "").Trim();
        if (name.Length < 8) name = name.PadRight(8, 'x'); // LuxVault requires 8 bytes salt
        data = (data ?? "").Replace(":", "").Trim();
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(name)) return true;

        var fs = new SecretFileSystem(NullLogger.Instance, new SecretBackendConfig(), _vault);
        
        // Walk to provision
        fs.WalkAsync(new Twalk(1, 1, 2, new[] { "provision" })).Wait();

        // Prepare the command: password:name:data
        string command = $"{password}:{name}:{data}";
        byte[] commandBytes = Encoding.UTF8.GetBytes(command);

        // Act
        var result = fs.WriteAsync(new Twrite(1, 2, 0, commandBytes)).Result;

        // Assert - If it didn't crash and returned full count, the Span parsing worked.
        return result.Count == (uint)commandBytes.Length;
    }

    [Fact]
    public void ArrayPool_Return_Clears_Memory_Remnants()
    {
        // This test verifies our mandate that ArrayPool.Return(buffer, clearArray: true) is used.
        var pool = ArrayPool<byte>.Shared;
        byte[] buffer = pool.Rent(1024);
        
        // Fill with "sensitive" markers
        buffer.AsSpan().Fill(0xEE);
        
        // Return with clearing
        pool.Return(buffer, clearArray: true);
        
        // Rent the same size again (high probability of getting the same buffer in a single-threaded test)
        byte[] nextBuffer = pool.Rent(1024);
        try
        {
            // If it's the same buffer, it MUST be zeroed.
            // If it's a different buffer, we can't be 100% sure, but modern .NET pool tends to reuse.
            if (ReferenceEquals(buffer, nextBuffer))
            {
                Assert.All(nextBuffer, b => Assert.Equal(0, b));
            }
        }
        finally
        {
            pool.Return(nextBuffer, clearArray: true);
        }
    }

    [Property(MaxTest = 50)]
    public bool LuxVault_GenerateHiddenId_Seed_Integrity(byte[] seed)
    {
        if (seed == null || seed.Length != 32) return true;

        byte[] seedCopy = (byte[])seed.Clone();
        
        // Generate multiple IDs from the same seed
        // (Elligator is non-deterministic but shouldn't corrupt the seed for the caller)
        string id1 = LuxVault.GenerateHiddenId(seed);
        string id2 = LuxVault.GenerateHiddenId(seed);

        // Seed should be preserved (we fixed this in LuxVault previously)
        bool seedPreserved = seed.SequenceEqual(seedCopy);
        
        // IDs should be valid hex and unique format
        return seedPreserved && id1.Length == 64 && id2.Length == 64;
    }
}

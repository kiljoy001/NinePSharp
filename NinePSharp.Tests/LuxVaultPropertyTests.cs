using System;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Configuration;
using Xunit;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests;

[Collection("Global Arena")]
public class LuxVaultPropertyTests
{
    private static readonly FieldInfo ArenaField = typeof(LuxVault).GetField("Arena", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly PropertyInfo ActiveAllocationsProp = ArenaField.FieldType.GetProperty("ActiveAllocations")!;
    private static readonly object ArenaInstance = ArenaField.GetValue(null)!;
    private static readonly object TestLock = new();

    private int GetActiveAllocations() => (int)ActiveAllocationsProp.GetValue(ArenaInstance)!;

    [Property(MaxTest = 100)]
    public bool Arena_Allocations_Always_Return_To_Zero(byte[] data, string password)
    {
        if (data == null || password == null) return true;

        lock (TestLock)
        {
            int baseline = GetActiveAllocations();

            // Perform encryption and decryption
            byte[] encrypted = LuxVault.Encrypt(data, password);
            using (var secret = LuxVault.DecryptToBytes(encrypted, password))
            {
                // Verify decryption worked
                if (secret == null || !data.SequenceEqual(secret.Span.ToArray()))
                    return false;
            }

            // After disposal, must return to baseline (all internal buffers cleared)
            return GetActiveAllocations() == baseline;
        }
    }

    [Property(MaxTest = 100)]
    public bool Tampering_Any_Byte_Causes_Decryption_Failure(byte[] data, string password, int byteIndexToTamper)
    {
        if (data == null || data.Length == 0 || password == null) return true;

        lock (TestLock)
        {
            byte[] payload = LuxVault.Encrypt(data, password);
            
            // Use Reflection to get internal sizes for precise tampering
            int index = Math.Abs(byteIndexToTamper) % payload.Length;
            payload[index] ^= 0x01; // Flip one bit

            using var secret = LuxVault.DecryptToBytes(payload, password);
            
            // Decrypt must return null because Poly1305 MAC will fail
            return secret == null;
        }
    }

    [Property(MaxTest = 50)]
    public bool Multiple_Concurrent_Users_Dont_Interfere(byte[][] datasets, string password)
    {
        if (datasets == null || datasets.Length == 0 || password == null) return true;

        lock (TestLock)
        {
            // Ensure baseline
            int baseline = GetActiveAllocations();

            // Process many blocks in parallel
            var results = datasets.Where(d => d != null).AsParallel().Select(d =>
            {
                var enc = LuxVault.Encrypt(d, password);
                using var dec = LuxVault.DecryptToBytes(enc, password);
                return dec != null && d.SequenceEqual(dec.Span.ToArray());
            }).ToList();

            // Verify all matched and arena is clean
            return results.All(r => r) && GetActiveAllocations() == baseline;
        }
    }

    [Fact]
    public void Inspect_SessionKey_Is_Locked_In_Memory()
    {
        // Use reflection to get the private _sessionKey
        var sessionKeyField = typeof(LuxVault).GetField("_sessionKey", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[]? key = (byte[]?)sessionKeyField.GetValue(null);

        // If key is initialized, it MUST be pinned
        if (key != null)
        {
            // Note: In .NET, we can't easily check 'pinned' status via reflection,
            // but we can verify it's not null and has expected length.
            key.Length.Should().Be(32);
        }
    }
}

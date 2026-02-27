using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Configuration;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

[Collection("Sequential Secret Tests")]
public class SecretFileSystemMutationPropertyFuzzTests
{
    private readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, byte TypeByte, string Name, int TotalSize);

    private static int _keysInitialized;
    private readonly ILuxVaultService _vault = new LuxVaultService();

    public SecretFileSystemMutationPropertyFuzzTests()
    {
        EnsureSessionKeys();
        SecretFileSystem.ClearSessionPasswords();
    }

    [Property(MaxTest = 30)]
    public void SecretFileSystem_Readdir_Root_Entries_Have_Valid_Wire_Layout(PositiveInt countSeed)
    {
        var fs = NewFs();
        uint count = (uint)Math.Clamp(countSeed.Get % 2048 + 24, 24, 4096);

        var page = fs.ReaddirAsync(new Treaddir(1, 1, 1, 0, count)).Sync();

        page.Count.Should().Be((uint)page.Data.Length);
        if (page.Data.Length == 0)
        {
            return;
        }

        var entries = ParseReaddir(page.Data.Span);
        entries.Should().NotBeEmpty();

        ulong expectedOffset = 0;
        foreach (var entry in entries)
        {
            expectedOffset += (ulong)entry.TotalSize;
            entry.NextOffset.Should().Be(expectedOffset);

            bool isDir = entry.Name == "vault";
            entry.QidType.Should().Be(isDir ? QidType.QTDIR : QidType.QTFILE);
            entry.TypeByte.Should().Be(isDir ? (byte)0x80 : (byte)0x00);
        }

        var names = entries.Select(e => e.Name).ToList();
        names.Should().OnlyContain(n => n == "provision" || n == "unlock" || n == "vault");
    }

    [Property(MaxTest = 25)]
    public void SecretFileSystem_Readdir_CountLimit_Only_Returns_Full_Entries(PositiveInt countSeed)
    {
        var fs = NewFs();

        var full = fs.ReaddirAsync(new Treaddir(1, 1, 1, 0, 8192)).Sync();
        var fullEntries = ParseReaddir(full.Data.Span);
        fullEntries.Should().NotBeEmpty();

        int limit = Math.Clamp(countSeed.Get % (full.Data.Length + 20), 1, full.Data.Length + 20);

        var limited = fs.ReaddirAsync(new Treaddir(2, 1, 1, 0, (uint)limit)).Sync();
        var limitedEntries = ParseReaddir(limited.Data.Span);

        int expectedBytes = 0;
        foreach (var entry in fullEntries)
        {
            if (expectedBytes + entry.TotalSize > limit)
            {
                break;
            }

            expectedBytes += entry.TotalSize;
        }

        limited.Data.Length.Should().Be(expectedBytes);
        limitedEntries.Select(e => e.Name)
            .Should().Equal(fullEntries.Take(limitedEntries.Count).Select(e => e.Name));
    }

    [Property(MaxTest = 15)]
    public void SecretFileSystem_Provision_Unlock_Remove_RoundTrip_Property(NonEmptyString rawName, NonEmptyString rawValue)
    {
        var password = "pw-" + Guid.NewGuid().ToString("N");
        var name = "s_" + Sanitize(rawName.Get, 20) + "_" + Guid.NewGuid().ToString("N")[..8];
        var value = "v_" + Sanitize(rawValue.Get, 30);

        ProvisionAsync(password, name, value).Sync();
        UnlockAsync(password, name).Sync();

        var beforeRemove = ReadVaultSecretAsync(name).Sync();
        beforeRemove.Should().Be(value);

        RemoveVaultSessionAsync(name).Sync();

        var afterRemove = ReadVaultSecretAsync(name).Sync();
        afterRemove.Should().BeEmpty();
    }

    [Fact]
    public async Task SecretFileSystem_Fuzz_Invalid_ControlPayloads_Do_Not_Throw()
    {
        var random = new Random(20260227);

        for (int i = 0; i < 160; i++)
        {
            var fs = NewFs();
            string controlFile = (i % 2 == 0) ? "provision" : "unlock";
            await fs.WalkAsync(new Twalk(1, 1, 1, new[] { controlFile }));

            int len = random.Next(0, 96);
            var bytes = new byte[len];
            random.NextBytes(bytes);

            // Keep payload intentionally malformed (no ':' separators)
            for (int j = 0; j < bytes.Length; j++)
            {
                if (bytes[j] == (byte)':')
                {
                    bytes[j] = (byte)'_';
                }
            }

            Func<Task> act = async () => _ = await fs.WriteAsync(new Twrite((ushort)(i + 1), 1, 0, bytes));
            await act.Should().NotThrowAsync();
        }
    }

    private async Task ProvisionAsync(string password, string name, string value)
    {
        var fs = NewFs();
        await fs.WalkAsync(new Twalk(1, 1, 1, new[] { "provision" }));

        byte[] payload = Encoding.UTF8.GetBytes($"{password}:{name}:{value}");
        try
        {
            var write = await fs.WriteAsync(new Twrite(1, 1, 0, payload));
            write.Count.Should().Be((uint)payload.Length);
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private async Task UnlockAsync(string password, string name)
    {
        var fs = NewFs();
        await fs.WalkAsync(new Twalk(1, 1, 1, new[] { "unlock" }));

        byte[] payload = Encoding.UTF8.GetBytes($"{password}:{name}");
        try
        {
            var write = await fs.WriteAsync(new Twrite(1, 1, 0, payload));
            write.Count.Should().Be((uint)payload.Length);
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private async Task<string> ReadVaultSecretAsync(string name)
    {
        var fs = NewFs();
        await fs.WalkAsync(new Twalk(1, 1, 1, new[] { "vault", name }));

        var read = await fs.ReadAsync(new Tread(2, 1, 0, 8192));
        return Encoding.UTF8.GetString(read.Data.Span);
    }

    private async Task RemoveVaultSessionAsync(string name)
    {
        var fs = NewFs();
        await fs.WalkAsync(new Twalk(1, 1, 1, new[] { "vault", name }));

        var removed = await fs.RemoveAsync(new Tremove(3, 1));
        removed.Tag.Should().Be(3);
    }

    private SecretFileSystem NewFs()
    {
        var config = new SecretBackendConfig { RootPath = "secrets-mutation-tests" };
        return new SecretFileSystem(NullLogger.Instance, config, _vault);
    }

    private static List<ReaddirEntry> ParseReaddir(ReadOnlySpan<byte> data)
    {
        var entries = new List<ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 24)
            {
                throw new Xunit.Sdk.XunitException($"Malformed readdir payload at offset {offset}");
            }

            int entryStart = offset;
            var qidType = (QidType)data[offset];
            offset += 1 + 4 + 8;

            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            byte typeByte = data[offset++];
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (data.Length - offset < nameLen)
            {
                throw new Xunit.Sdk.XunitException($"Invalid name length {nameLen} at offset {offset}");
            }

            string name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            int totalSize = offset - entryStart;
            entries.Add(new ReaddirEntry(qidType, nextOffset, typeByte, name, totalSize));
        }

        return entries;
    }

    private static string Sanitize(string value, int maxLen)
    {
        var clean = new string(value
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(maxLen)
            .ToArray());

        return string.IsNullOrWhiteSpace(clean) ? "x" : clean;
    }

    private static void EnsureSessionKeys()
    {
        if (Interlocked.Exchange(ref _keysInitialized, 1) == 1)
        {
            return;
        }

        byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        LuxVault.InitializeSessionKey(key);
        ProtectedSecret.InitializeSessionKey(key);
    }
}

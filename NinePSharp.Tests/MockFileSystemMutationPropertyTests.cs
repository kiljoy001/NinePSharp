using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class MockFileSystemMutationPropertyTests
{
    private static readonly LuxVaultService Vault = new();

    private readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, byte TypeByte, string Name);

    [Property(MaxTest = 40)]
    public void Readdir_Encodes_Sorted_Entries_With_Stable_Offsets(string[] rawNames)
    {
        if (rawNames == null) return;

        var cleanNames = rawNames
            .Select((n, i) => CleanName(n, i))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        if (cleanNames.Count < 3) return;

        var fs = new MockFileSystem(Vault);
        var dirName = $"{cleanNames[0]}_dir";
        fs.MkdirAsync(new Tmkdir(100, 1, 1, dirName, 0755, 0)).Sync();

        foreach (var (index, name) in cleanNames.Skip(1).Select((name, i) => (i + 2, name)))
        {
            fs.LcreateAsync(new Tlcreate(100, (ushort)index, 1, name, 0, 0644, 0)).Sync();
        }

        var response = fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 65535)).Sync();
        var entries = ParseReaddirEntries(response.Data.Span);

        entries.Should().NotBeEmpty();
        response.Count.Should().Be((uint)response.Data.Length);
        response.Size.Should().Be((uint)(response.Count + NinePConstants.HeaderSize + 4));

        var expectedNames = new List<string> { dirName };
        expectedNames.AddRange(cleanNames.Skip(1));
        expectedNames = expectedNames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n)
            .ToList();

        entries.Select(e => e.Name).Should().Equal(expectedNames);

        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].NextOffset.Should().Be((ulong)(i + 1));
            entries[i].TypeByte.Should().Be((byte)entries[i].QidType);

            if (entries[i].Name == dirName)
            {
                entries[i].QidType.Should().Be(QidType.QTDIR);
            }
            else
            {
                entries[i].QidType.Should().Be(QidType.QTFILE);
            }
        }
    }

    [Property(MaxTest = 40)]
    public void Statfs_Uses_Total_Content_Length_For_Blocks(PositiveInt lengthA, PositiveInt lengthB)
    {
        int lenA = (lengthA.Get % 12000) + 32;
        int lenB = (lengthB.Get % 12000) + 64;
        if (lenA == lenB) lenB++;

        var fs = new MockFileSystem(Vault);
        CreateAndWriteFile(fs, "alpha.bin", lenA, 10);
        CreateAndWriteFile(fs, "beta.bin", lenB, 20);

        var statfs = fs.StatfsAsync(new Tstatfs(100, 1, 1)).Sync();

        var expectedUsedBlocks = (ulong)((lenA + lenB) / 4096);
        statfs.Blocks.Should().Be(100000);
        statfs.BFree.Should().Be(100000UL - expectedUsedBlocks);
        statfs.BAvail.Should().Be(100000UL - expectedUsedBlocks);
        statfs.Files.Should().Be(3); // root + 2 files
        statfs.FFree.Should().Be(100000UL - 3UL);
    }

    [Property(MaxTest = 30)]
    public void Wstat_And_GetAttr_Mode_Agree_And_Honor_Sentinel(bool useSentinel, byte lowBits)
    {
        var fs = new MockFileSystem(Vault);
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "mode.bin", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(1, 1, 1, new[] { "mode.bin" })).Sync();

        uint requestedMode = useSentinel ? 0xFFFFFFFFu : (uint)(0x180 | (lowBits & 0x7Fu));
        var writeStat = new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), requestedMode, 0, 0, 0, "mode.bin", "u", "g", "m");
        fs.WstatAsync(new Twstat(1, 1, writeStat)).Sync();

        var stat = fs.StatAsync(new Tstat(1, 1)).Sync().Stat;
        var getattr = fs.GetAttrAsync(new Tgetattr(1, 1, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC)).Sync();

        if (useSentinel)
        {
            stat.Mode.Should().Be(0644u);
            getattr.Mode.Should().Be(0644u);
        }
        else
        {
            stat.Mode.Should().Be(requestedMode);
            getattr.Mode.Should().Be(requestedMode);
        }
    }

    [Property(MaxTest = 30)]
    public void Readdir_Offset_Skips_Expected_Entries(PositiveInt fileCount, PositiveInt offsetSeed)
    {
        int count = Math.Clamp(fileCount.Get, 3, 8);
        var fs = new MockFileSystem(Vault);

        for (int i = 0; i < count; i++)
        {
            fs.LcreateAsync(new Tlcreate(100, (ushort)(i + 1), 1, $"f{i}", 0, 0644, 0)).Sync();
        }

        var all = fs.ReaddirAsync(new Treaddir(100, 1, 1, 0, 65535)).Sync();
        var allEntries = ParseReaddirEntries(all.Data.Span);
        int skip = Math.Min(offsetSeed.Get % allEntries.Count, allEntries.Count - 1);

        var paged = fs.ReaddirAsync(new Treaddir(100, 1, 1, (ulong)skip, 65535)).Sync();
        var pagedEntries = ParseReaddirEntries(paged.Data.Span);

        pagedEntries.Should().NotBeEmpty();
        pagedEntries[0].NextOffset.Should().Be((ulong)(skip + 1));
        pagedEntries.Select(e => e.Name).Should().Equal(allEntries.Skip(skip).Select(e => e.Name));
    }

    [Fact]
    public void Remove_Rejects_Root_Path()
    {
        var fs = new MockFileSystem(Vault);
        Action act = () => fs.RemoveAsync(new Tremove(1, 1)).Sync();
        act.Should().Throw<NinePProtocolException>().WithMessage("*Cannot remove root*");
    }

    private static void CreateAndWriteFile(MockFileSystem fs, string name, int size, ushort tag)
    {
        fs.LcreateAsync(new Tlcreate(100, tag, 1, name, 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(tag, 1, 1, new[] { name })).Sync();
        fs.WriteAsync(new Twrite(tag, 1, 0, new byte[size])).Sync();
        fs.WalkAsync(new Twalk(tag, 1, 1, new[] { ".." })).Sync();
    }

    private static string CleanName(string? raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"n{index}";
        var chars = raw
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(24)
            .ToArray();

        var clean = chars.Length == 0 ? $"n{index}" : new string(chars);
        if (clean == "..") clean = $"n{index}";
        return clean;
    }

    private static List<ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var entries = new List<ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 24)
            {
                throw new Xunit.Sdk.XunitException($"Malformed entry at byte {offset}, remaining {data.Length - offset}");
            }

            var qidType = (QidType)data[offset];
            offset += 1 + 4 + 8; // qid.type + qid.version + qid.path

            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            byte typeByte = data[offset++];
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (nameLen > data.Length - offset)
            {
                throw new Xunit.Sdk.XunitException($"Invalid name length {nameLen} at byte {offset}");
            }

            string name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            entries.Add(new ReaddirEntry(qidType, nextOffset, typeByte, name));
        }

        return entries;
    }
}

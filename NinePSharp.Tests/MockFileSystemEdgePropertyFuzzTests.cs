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
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class MockFileSystemEdgePropertyFuzzTests
{
    private const int ReaddirEntryOverheadBytes = 24; // qid[13] + offset[8] + type[1] + nameLen[2]
    private static readonly ILuxVaultService Vault = new LuxVaultService();

    private readonly record struct ReaddirEntry(
        QidType QidType,
        uint QidVersion,
        ulong QidPath,
        ulong NextOffset,
        byte TypeByte,
        string Name,
        int SerializedLength);

    [Property(MaxTest = 45)]
    public void Walk_Missing_Path_Uses_Fallback_Qid_And_Stat_GetAttr_Fallbacks(NonEmptyString rawName, NinePDialect dialect)
    {
        var name = CleanName(rawName.Get, "missing");
        var fs = new MockFileSystem(Vault) { Dialect = dialect };

        var walk = fs.WalkAsync(new Twalk(1, 1, 1, new[] { name })).Sync();

        walk.Wqid.Should().ContainSingle();
        walk.Wqid[0].Type.Should().Be(QidType.QTDIR);
        walk.Wqid[0].Path.Should().Be((ulong)name.GetHashCode());

        var stat = fs.StatAsync(new Tstat(2, 1)).Sync().Stat;
        stat.Name.Should().Be(name);
        stat.Qid.Type.Should().Be(QidType.QTFILE);
        stat.Dialect.Should().Be(dialect);

        var getattr = fs.GetAttrAsync(new Tgetattr(3, 1, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC))
            .GetAwaiter()
            .GetResult();
        getattr.Qid.Type.Should().Be(QidType.QTFILE);
        getattr.Mode.Should().Be(0644u);
    }

    [Fact]
    public void Walk_Existing_File_And_Directory_Report_Correct_Qid_Types()
    {
        var fs = new MockFileSystem(Vault);
        fs.MkdirAsync(new Tmkdir(100, 1, 1, "dir", 0755, 0)).Sync();
        fs.LcreateAsync(new Tlcreate(100, 2, 1, "file.bin", 0, 0644, 0)).Sync();

        var dirWalk = fs.WalkAsync(new Twalk(1, 1, 1, new[] { "dir" })).Sync();
        dirWalk.Wqid.Should().ContainSingle();
        dirWalk.Wqid[0].Type.Should().Be(QidType.QTDIR);

        fs.WalkAsync(new Twalk(2, 1, 1, new[] { ".." })).Sync();
        var fileWalk = fs.WalkAsync(new Twalk(3, 1, 1, new[] { "file.bin" })).Sync();
        fileWalk.Wqid.Should().ContainSingle();
        fileWalk.Wqid[0].Type.Should().Be(QidType.QTFILE);
    }

    [Property(MaxTest = 40)]
    public void Write_Read_Offset_Fuzz_Matches_Reference_Model(PositiveInt seed, PositiveInt operationCount)
    {
        var fs = new MockFileSystem(Vault);
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "rw.bin", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(2, 1, 1, new[] { "rw.bin" })).Sync();

        var expected = Array.Empty<byte>();

        // Deterministic steps that force both append and overwrite branches.
        expected = ApplyWriteAndAssert(fs, expected, offset: 0, new byte[] { 0x01, 0x02, 0x03, 0x04 }, tag: 10);
        expected = ApplyWriteAndAssert(fs, expected, offset: 8, new byte[] { 0xAA, 0xBB }, tag: 11);
        expected = ApplyWriteAndAssert(fs, expected, offset: 2, new byte[] { 0x10, 0x11, 0x12 }, tag: 12);

        var rng = new Random(seed.Get);
        int ops = Math.Clamp(operationCount.Get % 18, 3, 17);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, expected.Length + 9);
            int len = rng.Next(1, 33);
            var data = new byte[len];
            rng.NextBytes(data);

            expected = ApplyWriteAndAssert(fs, expected, offset, data, (ushort)(100 + i));
        }

        var all = fs.ReadAsync(new Tread(200, 1, 0, (uint)(expected.Length + 32))).Sync();
        all.Data.ToArray().Should().Equal(expected);

        // Read exactly at EOF and beyond EOF should always be empty.
        var eof = fs.ReadAsync(new Tread(201, 1, (ulong)expected.Length, 64)).Sync();
        eof.Data.Length.Should().Be(0);
        var beyond = fs.ReadAsync(new Tread(202, 1, (ulong)(expected.Length + 25), 64)).Sync();
        beyond.Data.Length.Should().Be(0);

        if (expected.Length > 1)
        {
            int mid = expected.Length / 2;
            var tail = fs.ReadAsync(new Tread(203, 1, (ulong)mid, uint.MaxValue)).Sync();
            tail.Data.ToArray().Should().Equal(expected.Skip(mid).ToArray());
        }
    }

    [Fact]
    public void Write_To_Directory_Is_Ignored_And_Directory_Length_Remains_Zero()
    {
        var fs = new MockFileSystem(Vault);
        fs.MkdirAsync(new Tmkdir(100, 1, 1, "bucket", 0755, 0)).Sync();
        fs.WalkAsync(new Twalk(1, 1, 1, new[] { "bucket" })).Sync();

        fs.WriteAsync(new Twrite(2, 1, 0, Encoding.UTF8.GetBytes("should-not-stick"))).Sync();

        var stat = fs.StatAsync(new Tstat(3, 1)).Sync().Stat;
        stat.Length.Should().Be(0);
        ((stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0).Should().BeTrue();
    }

    [Fact]
    public void Mkdir_From_NonRoot_Creates_Child_Under_Current_Path()
    {
        var fs = new MockFileSystem(Vault);
        fs.MkdirAsync(new Tmkdir(100, 1, 1, "parent", 0755, 0)).Sync();
        fs.WalkAsync(new Twalk(2, 1, 1, new[] { "parent" })).Sync();

        var mkdir = fs.MkdirAsync(new Tmkdir(100, 3, 1, "child", 0755, 0)).Sync();
        var expectedQidPath = (ulong)("/parent/child".GetHashCode() & 0x7FFFFFFF);
        mkdir.Qid.Path.Should().Be(expectedQidPath);
        mkdir.Qid.Type.Should().Be(QidType.QTDIR);

        var childEntries = ParseReaddirEntries(fs.ReaddirAsync(new Treaddir(100, 4, 1, 0, 65535))
            .Sync()
            .Data
            .Span);
        childEntries.Select(e => e.Name).Should().ContainSingle().Which.Should().Be("child");

        fs.WalkAsync(new Twalk(5, 1, 1, new[] { ".." })).Sync();
        var rootEntries = ParseReaddirEntries(fs.ReaddirAsync(new Treaddir(100, 6, 1, 0, 65535))
            .Sync()
            .Data
            .Span);
        rootEntries.Select(e => e.Name).Should().Contain("parent");
        rootEntries.Select(e => e.Name).Should().NotContain("child");
    }

    [Fact]
    public void Create_Implicitly_Opens_File_And_Rejects_Directory_Perm()
    {
        var fs = new MockFileSystem(Vault);
        fs.CreateAsync(BuildTcreate(1, 1, "created.bin", 0644, 0)).Sync();

        var payload = Encoding.UTF8.GetBytes("created-through-current-path");
        fs.WriteAsync(new Twrite(2, 1, 0, payload)).Sync();
        var read = fs.ReadAsync(new Tread(3, 1, 0, 8192)).Sync();
        read.Data.ToArray().Should().Equal(payload);

        fs.WalkAsync(new Twalk(4, 1, 1, new[] { ".." })).Sync();
        Action act = () => fs.CreateAsync(BuildTcreate(5, 1, "dir-via-create", (uint)NinePConstants.FileMode9P.DMDIR, 0))
            .GetAwaiter()
            .GetResult();
        act.Should().Throw<NinePProtocolException>().WithMessage("*Cannot create directory with Tcreate*");
    }

    [Fact]
    public void Create_And_Lcreate_Fail_When_Parent_Is_Not_Directory()
    {
        var fs = new MockFileSystem(Vault);
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "anchor.bin", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(2, 1, 1, new[] { "anchor.bin" })).Sync();

        Action create = () => fs.CreateAsync(BuildTcreate(3, 1, "child.bin", 0644, 0))
            .GetAwaiter()
            .GetResult();
        create.Should().Throw<NinePProtocolException>().WithMessage("*Parent directory does not exist*");

        Action lcreate = () => fs.LcreateAsync(new Tlcreate(100, 4, 1, "child2.bin", 0, 0644, 0))
            .GetAwaiter()
            .GetResult();
        lcreate.Should().Throw<NinePProtocolException>().WithMessage("*Parent directory does not exist*");
    }

    [Fact]
    public void Rename_And_Renameat_Reject_Missing_Source()
    {
        var fs = new MockFileSystem(Vault);
        fs.WalkAsync(new Twalk(1, 1, 1, new[] { "missing-source" })).Sync();

        Action rename = () => fs.RenameAsync(new Trename(100, 1, 1, 1, "new.bin"))
            .GetAwaiter()
            .GetResult();
        rename.Should().Throw<NinePProtocolException>().WithMessage("*Source file does not exist*");

        Action renameat = () => fs.RenameatAsync(new Trenameat(100, 2, 1, "old.bin", 1, "new.bin"))
            .GetAwaiter()
            .GetResult();
        renameat.Should().Throw<NinePProtocolException>().WithMessage("*Source file does not exist*");
    }

    [Fact]
    public void Remove_NonRoot_Succeeds_Then_Throws_NotFound_And_Root_Remove_Is_Rejected()
    {
        var fs = new MockFileSystem(Vault);
        fs.LcreateAsync(new Tlcreate(100, 1, 1, "delete.me", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(2, 1, 1, new[] { "delete.me" })).Sync();

        var removed = fs.RemoveAsync(new Tremove(3, 1)).Sync();
        removed.Tag.Should().Be(3);

        Action secondRemove = () => fs.RemoveAsync(new Tremove(4, 1)).Sync();
        secondRemove.Should().Throw<NinePProtocolException>().WithMessage("*File not found*");

        fs.WalkAsync(new Twalk(5, 1, 1, new[] { ".." })).Sync();
        Action rootRemove = () => fs.RemoveAsync(new Tremove(6, 1)).Sync();
        rootRemove.Should().Throw<NinePProtocolException>().WithMessage("*Cannot remove root*");
    }

    [Fact]
    public void Stat_And_GetAttr_Set_Directory_Bit_Only_For_Directories()
    {
        var fs = new MockFileSystem(Vault);
        fs.MkdirAsync(new Tmkdir(100, 1, 1, "dirmode", 0755, 0)).Sync();
        fs.LcreateAsync(new Tlcreate(100, 2, 1, "filemode", 0, 0644, 0)).Sync();

        fs.WalkAsync(new Twalk(3, 1, 1, new[] { "dirmode" })).Sync();
        var dirStat = fs.StatAsync(new Tstat(4, 1)).Sync().Stat;
        var dirAttr = fs.GetAttrAsync(new Tgetattr(5, 1, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC)).Sync();
        dirStat.Qid.Type.Should().Be(QidType.QTDIR);
        dirAttr.Qid.Type.Should().Be(QidType.QTDIR);
        ((dirStat.Mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0).Should().BeTrue();
        ((dirAttr.Mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0).Should().BeTrue();

        fs.WalkAsync(new Twalk(6, 1, 1, new[] { ".." })).Sync();
        fs.WalkAsync(new Twalk(7, 1, 1, new[] { "filemode" })).Sync();
        var fileStat = fs.StatAsync(new Tstat(8, 1)).Sync().Stat;
        var fileAttr = fs.GetAttrAsync(new Tgetattr(9, 1, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC)).Sync();
        fileStat.Qid.Type.Should().Be(QidType.QTFILE);
        fileAttr.Qid.Type.Should().Be(QidType.QTFILE);
        ((fileStat.Mode & (uint)NinePConstants.FileMode9P.DMDIR) == 0).Should().BeTrue();
        ((fileAttr.Mode & (uint)NinePConstants.FileMode9P.DMDIR) == 0).Should().BeTrue();
    }

    [Fact]
    public void Statfs_Uses_Summed_Content_Length_And_Tracks_Free_Counts()
    {
        var fs = new MockFileSystem(Vault);

        fs.LcreateAsync(new Tlcreate(100, 1, 1, "alpha.bin", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(2, 1, 1, new[] { "alpha.bin" })).Sync();
        fs.WriteAsync(new Twrite(3, 1, 0, new byte[5000])).Sync();
        fs.WalkAsync(new Twalk(4, 1, 1, new[] { ".." })).Sync();

        fs.LcreateAsync(new Tlcreate(100, 5, 1, "beta.bin", 0, 0644, 0)).Sync();
        fs.WalkAsync(new Twalk(6, 1, 1, new[] { "beta.bin" })).Sync();
        fs.WriteAsync(new Twrite(7, 1, 0, new byte[5000])).Sync();
        fs.WalkAsync(new Twalk(8, 1, 1, new[] { ".." })).Sync();

        var statfs = fs.StatfsAsync(new Tstatfs(100, 9, 1)).Sync();

        statfs.Blocks.Should().Be(100000UL);
        statfs.BFree.Should().Be(99998UL);
        statfs.BAvail.Should().Be(99998UL);
        statfs.Files.Should().Be(3UL); // root + two files
        statfs.FFree.Should().Be(99997UL);
    }

    [Property(MaxTest = 35)]
    public void Readdir_Fuzzed_Pagination_And_Binary_Encoding_Are_Stable(PositiveInt seed, PositiveInt countSeed, PositiveInt pageSeed)
    {
        var fs = new MockFileSystem(Vault);
        var rng = new Random(seed.Get);
        int count = Math.Clamp(countSeed.Get % 12, 4, 12);

        var specs = new List<(string Name, bool IsDirectory)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            bool isDir = i % 3 == 0;
            string raw = $"{(isDir ? "d" : "f")}_{rng.Next(0, int.MaxValue):x8}";
            string name = CleanName(raw, $"n{i}");
            while (!seen.Add(name))
            {
                name = $"{name}_{i}";
            }

            specs.Add((name, isDir));
            if (isDir)
            {
                fs.MkdirAsync(new Tmkdir(100, (ushort)(10 + i), 1, name, 0755, 0)).Sync();
            }
            else
            {
                fs.LcreateAsync(new Tlcreate(100, (ushort)(20 + i), 1, name, 0, 0644, 0)).Sync();
            }
        }

        var full = fs.ReaddirAsync(new Treaddir(100, 200, 1, 0, 65535)).Sync();
        var fullEntries = ParseReaddirEntries(full.Data.Span);

        full.Count.Should().Be((uint)full.Data.Length);
        full.Size.Should().Be((uint)(full.Count + NinePConstants.HeaderSize + 4));
        fullEntries.Should().HaveCount(specs.Count);

        var expected = specs.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
        fullEntries.Select(e => e.Name).Should().Equal(expected.Select(e => e.Name));

        for (int i = 0; i < fullEntries.Count; i++)
        {
            var entry = fullEntries[i];
            var expectedInfo = expected[i];

            entry.QidVersion.Should().Be(0u);
            entry.NextOffset.Should().Be((ulong)(i + 1));
            entry.TypeByte.Should().Be((byte)entry.QidType);
            entry.SerializedLength.Should().Be(ReaddirEntryOverheadBytes + Encoding.UTF8.GetByteCount(entry.Name));
            entry.QidPath.Should().Be((ulong)($"/{entry.Name}".GetHashCode() & 0x7FFFFFFF));

            if (expectedInfo.IsDirectory)
            {
                entry.QidType.Should().Be(QidType.QTDIR);
            }
            else
            {
                entry.QidType.Should().Be(QidType.QTFILE);
            }
        }

        int skip = pageSeed.Get % fullEntries.Count;
        var paged = fs.ReaddirAsync(new Treaddir(100, 201, 1, (ulong)skip, 65535)).Sync();
        var pagedEntries = ParseReaddirEntries(paged.Data.Span);
        pagedEntries.Select(e => e.Name).Should().Equal(fullEntries.Skip(skip).Select(e => e.Name));

        int take = (pageSeed.Get % fullEntries.Count) + 1;
        int exactCount = fullEntries.Take(take).Sum(e => e.SerializedLength);
        var bounded = fs.ReaddirAsync(new Treaddir(100, 202, 1, 0, (uint)exactCount)).Sync();
        var boundedEntries = ParseReaddirEntries(bounded.Data.Span);

        bounded.Count.Should().Be((uint)exactCount);
        bounded.Size.Should().Be((uint)(bounded.Count + NinePConstants.HeaderSize + 4));
        boundedEntries.Select(e => e.Name).Should().Equal(fullEntries.Take(take).Select(e => e.Name));
    }

    private static byte[] ApplyWriteAndAssert(MockFileSystem fs, byte[] current, int offset, byte[] data, ushort tag)
    {
        fs.WriteAsync(new Twrite(tag, 1, (ulong)offset, data)).Sync().Count.Should().Be((uint)data.Length);
        var next = new byte[Math.Max(current.Length, offset + data.Length)];
        Array.Copy(current, next, current.Length);
        Array.Copy(data, 0, next, offset, data.Length);
        return next;
    }

    private static string CleanName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var chars = raw
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(32)
            .ToArray();

        if (chars.Length == 0)
        {
            return fallback;
        }

        var name = new string(chars);
        return name == ".." ? fallback : name;
    }

    private static List<ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var entries = new List<ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < ReaddirEntryOverheadBytes)
            {
                throw new Xunit.Sdk.XunitException($"Malformed readdir payload at offset {offset}.");
            }

            int start = offset;

            var qidType = (QidType)data[offset++];
            uint qidVersion = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
            ulong qidPath = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            byte typeByte = data[offset++];
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (nameLen > data.Length - offset)
            {
                throw new Xunit.Sdk.XunitException($"Invalid name length {nameLen} at offset {offset}.");
            }

            string name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            entries.Add(new ReaddirEntry(
                qidType,
                qidVersion,
                qidPath,
                nextOffset,
                typeByte,
                name,
                offset - start));
        }

        return entries;
    }

    private static Tcreate BuildTcreate(ushort tag, uint fid, string name, uint perm, byte mode)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        uint size = (uint)(NinePConstants.HeaderSize + 4 + 2 + nameBytes.Length + 4 + 1);
        var data = new byte[size];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), size);
        data[4] = (byte)MessageTypes.Tcreate;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), tag);

        int offset = NinePConstants.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), fid);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)nameBytes.Length);
        offset += 2;
        nameBytes.CopyTo(data.AsSpan(offset, nameBytes.Length));
        offset += nameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), perm);
        offset += 4;
        data[offset] = mode;

        return new Tcreate(data);
    }
}

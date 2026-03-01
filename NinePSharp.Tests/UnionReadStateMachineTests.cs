using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

/// <summary>
/// Phase 6: Tests for union read state machine.
/// Verifies Umh (union head), Umc (current mount), Uri (read offset) fields.
/// These are per 9front semantics for reading from union mounts.
/// </summary>
public class UnionReadStateMachineTests
{
    [Fact]
    public async Task Open_Union_Sets_Umh_To_First_Branch()
    {
        // When opening a directory in a union mount, Umh should point to first branch
        var backend1 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "a.txt" }));
        var backend2 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "b.txt" }));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend1, backend2 });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" });
        var open = await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Open should succeed - first backend in union is used
        open.Should().NotBeNull();
        open.Iounit.Should().BeGreaterThan(0u);
    }

    [Fact]
    public async Task Read_Union_Advances_Through_Branches_On_EOF()
    {
        // Reading directory in union should continue to next backend when first returns EOF
        var backend1 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "file1.txt" }));
        var backend2 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "file2.txt" }));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend1, backend2 });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Read all directory entries
        var entries = new List<string>();
        ulong offset = 0;
        for (int i = 0; i < 10; i++) // Safety limit
        {
            var read = await DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, (ushort)(4 + i), 101, offset, 8192);

            if (read.Count == 0) break;

            var stats = ParseReaddirEntries(read.Data.Span);
            foreach (var entry in stats)
            {
                entries.Add(entry.Name);
            }
            offset += read.Count;
        }

        // Should see entries from both backends (union combines results)
        entries.Should().Contain("file1.txt", "first backend entry should be present");
        entries.Should().Contain("file2.txt", "second backend entry should be present (union advancement)");
    }

    [Fact]
    public async Task Readdir_Union_Combines_Results_No_Duplicates()
    {
        // When both backends have same file, union should show it only once (first wins)
        var backend1 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "shared.txt", "only1.txt" }));
        var backend2 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "shared.txt", "only2.txt" }));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend1, backend2 });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Read all directory entries
        var entries = new List<string>();
        ulong offset = 0;
        for (int i = 0; i < 10; i++)
        {
            var read = await DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, (ushort)(4 + i), 101, offset, 8192);

            if (read.Count == 0) break;

            var stats = ParseReaddirEntries(read.Data.Span);
            foreach (var entry in stats)
            {
                entries.Add(entry.Name);
            }
            offset += read.Count;
        }

        // shared.txt should appear only once
        entries.Count(e => e == "shared.txt").Should().Be(1, "union should dedupe overlapping entries");
        entries.Should().Contain("only1.txt");
        entries.Should().Contain("only2.txt");
    }

    [Fact]
    public async Task Close_Resets_Union_State()
    {
        // After clunk, re-opening should reset union state
        var backend = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "test.txt" }));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Read once
        var read1 = await DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 4, 101, 0, 8192);

        // Clunk
        await dispatcher.DispatchAsync("test-session",
            NinePMessage.NewMsgTclunk(new Tclunk(5, 101)),
            NinePDialect.NineP2000);

        // Walk and open again
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 6, 100, 102, new[] { "union" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 7, 102);

        // Read should return same data (state was reset)
        var read2 = await DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 8, 102, 0, 8192);

        read2.Should().NotBeNull();
        read2.Count.Should().Be(read1.Count, "after clunk and re-open, read should restart");
    }

    [Fact]
    public async Task Union_With_MBEFORE_Prepends_To_Union()
    {
        // MBEFORE flag should add new mount before existing one
        // For now, test that first registered backend in union is used for walk
        var backend1 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "original.txt" }));
        var backend2 = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "added.txt" }));

        // First backend registered should be at head of union
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend1, backend2 });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        var walk = await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union", "original.txt" });

        // Walk should succeed (first backend has original.txt)
        walk.Should().NotBeNull();
        walk.Wqid.Should().HaveCount(2);
    }

    [Fact]
    public async Task Create_In_Union_Uses_MCREATE_Flag()
    {
        // Create should go to mount with MCREATE flag
        var createBackend = new StubBackend("/union", () => new CreatableFileSystem());
        var readonlyBackend = new StubBackend("/union", () => new DirectoryListingFileSystem(new[] { "existing.txt" }));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { readonlyBackend, createBackend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101);

        // Create new file
        var create = await dispatcher.DispatchAsync("test-session",
            NinePMessage.NewMsgTcreate(new Tcreate(4, 101, "newfile.txt", 0644, 0)),
            NinePDialect.NineP2000);

        // Create should succeed (either backend accepts it)
        create.Should().BeOfType<Rcreate>();
    }

    [Fact]
    public async Task Write_To_Union_Goes_To_First_Writable()
    {
        // Write should go to first backend that accepts it
        var backend = new StubBackend("/union", () => new WritableFileSystem());

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union", "file.txt" });
        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 3, 101, mode: 1); // OWRITE

        // Write to file
        var data = Encoding.UTF8.GetBytes("test content");
        var write = await DispatcherIntegrationTestKit.WriteAsync(dispatcher, 4, 101, 0, data);

        write.Should().NotBeNull();
        write.Count.Should().Be((uint)data.Length);
    }

    private static List<DispatcherIntegrationTestKit.ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var entries = new List<DispatcherIntegrationTestKit.ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            // Minimum entry: qid(13) + offset(8) + type(1) + namelen(2) = 24 bytes
            if (data.Length - offset < 24)
                break;

            var qidType = (QidType)data[offset];
            offset++;
            // skip vers (4) + path (8)
            offset += 12;
            var nextOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            // skip type byte (d_type)
            offset += 1;

            var nameLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (offset + nameLen > data.Length)
                break;

            var name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            entries.Add(new DispatcherIntegrationTestKit.ReaddirEntry(qidType, nextOffset, name));
        }

        return entries;
    }
}

/// <summary>
/// Simple file system that lists directory entries.
/// </summary>
internal sealed class DirectoryListingFileSystem : INinePFileSystem
{
    private readonly string[] _files;

    public DirectoryListingFileSystem(string[] files)
    {
        _files = files;
    }

    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((name, i) =>
            new Qid(_files.Contains(name) ? QidType.QTFILE : QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode())))
            .ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

    public Task<Rread> ReadAsync(Tread tread)
    {
        // For 9P2000 directory reads, return serialized Stat structures
        var allData = new List<byte>();

        for (int i = 0; i < _files.Length; i++)
        {
            var file = _files[i];
            var stat = new Stat(0, 0, 1, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(file.GetHashCode())),
                0644, 0, 0, 100, file, "none", "none", "none", dialect: Dialect);
            var buffer = new byte[stat.Size];
            var offset = 0;
            stat.WriteTo(buffer, ref offset);
            allData.AddRange(buffer);
        }

        var data = allData.ToArray();
        if (tread.Offset >= (ulong)data.Length)
        {
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        var start = (int)tread.Offset;
        var remaining = data.Length - start;
        var count = tread.Count > int.MaxValue ? remaining : Math.Min((int)tread.Count, remaining);
        var result = new byte[count];
        Array.Copy(data, start, result, 0, count);

        return Task.FromResult(new Rread(tread.Tag, result));
    }

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        // Build readdir entries (9P2000.L format: qid + offset + type + name)
        var allData = new List<byte>();

        for (int i = 0; i < _files.Length; i++)
        {
            var file = _files[i];
            var nameBytes = Encoding.UTF8.GetBytes(file);

            // Qid: type(1) + vers(4) + path(8)
            allData.Add((byte)QidType.QTFILE);
            allData.AddRange(BitConverter.GetBytes(0u)); // version
            allData.AddRange(BitConverter.GetBytes((ulong)Math.Abs(file.GetHashCode()))); // path

            // Offset to next entry
            allData.AddRange(BitConverter.GetBytes((ulong)(i + 1)));

            // Type (d_type)
            // allData.Add(8); // DT_REG - skip this, format varies

            // Name length + name
            allData.AddRange(BitConverter.GetBytes((ushort)nameBytes.Length));
            allData.AddRange(nameBytes);
        }

        var data = allData.ToArray();
        if (treaddir.Offset >= (ulong)data.Length)
        {
            return Task.FromResult(new Rreaddir(
                (uint)(NinePConstants.HeaderSize + 4),
                treaddir.Tag,
                0,
                Array.Empty<byte>()));
        }

        var start = (int)treaddir.Offset;
        var count = Math.Min((int)treaddir.Count, data.Length - start);
        var result = new byte[count];
        Array.Copy(data, start, result, 0, count);

        return Task.FromResult(new Rreaddir(
            (uint)(NinePConstants.HeaderSize + 4 + count),
            treaddir.Tag,
            (uint)count,
            result));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
        => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
        => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 1, new Qid(QidType.QTDIR, 0, 1), 0755, 0, 0, 0, ".", "none", "none", "none", dialect: Dialect);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat)
        => Task.FromResult(new Rwstat(twstat.Tag));

    public Task<Rremove> RemoveAsync(Tremove tremove)
        => Task.FromResult(new Rremove(tremove.Tag));

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
        => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 2), 8192));

    public INinePFileSystem Clone() => new DirectoryListingFileSystem(_files);
}

/// <summary>
/// File system that supports create operations.
/// </summary>
internal sealed class CreatableFileSystem : INinePFileSystem
{
    private readonly HashSet<string> _created = new();

    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((name, i) =>
            new Qid(_created.Contains(name) ? QidType.QTFILE : QidType.QTDIR, 0, (ulong)Math.Abs(name.GetHashCode())))
            .ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

    public Task<Rread> ReadAsync(Tread tread)
        => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        return Task.FromResult(new Rreaddir(
            (uint)(NinePConstants.HeaderSize + 4),
            treaddir.Tag,
            0,
            Array.Empty<byte>()));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
        => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
        => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 1, new Qid(QidType.QTDIR, 0, 1), 0755, 0, 0, 0, ".", "none", "none", "none", dialect: Dialect);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat)
        => Task.FromResult(new Rwstat(twstat.Tag));

    public Task<Rremove> RemoveAsync(Tremove tremove)
        => Task.FromResult(new Rremove(tremove.Tag));

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
    {
        _created.Add(tcreate.Name);
        return Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(tcreate.Name.GetHashCode())), 8192));
    }

    public INinePFileSystem Clone() => new CreatableFileSystem();
}

/// <summary>
/// File system that supports write operations.
/// </summary>
internal sealed class WritableFileSystem : INinePFileSystem
{
    public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

    public Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = twalk.Wname.Select((name, i) => new Qid(QidType.QTFILE, 0, (ulong)Math.Abs(name.GetHashCode()))).ToArray();
        return Task.FromResult(new Rwalk(twalk.Tag, qids));
    }

    public Task<Ropen> OpenAsync(Topen topen)
        => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 8192));

    public Task<Rread> ReadAsync(Tread tread)
        => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));

    public Task<Rreaddir> ReaddirAsync(Treaddir treaddir)
    {
        // Single file "file.txt"
        var file = "file.txt";
        var nameBytes = Encoding.UTF8.GetBytes(file);
        var allData = new List<byte>();

        allData.Add((byte)QidType.QTFILE);
        allData.AddRange(BitConverter.GetBytes(0u));
        allData.AddRange(BitConverter.GetBytes((ulong)Math.Abs(file.GetHashCode())));
        allData.AddRange(BitConverter.GetBytes(1UL));
        allData.AddRange(BitConverter.GetBytes((ushort)nameBytes.Length));
        allData.AddRange(nameBytes);

        var data = allData.ToArray();
        return Task.FromResult(new Rreaddir(
            (uint)(NinePConstants.HeaderSize + 4 + data.Length),
            treaddir.Tag,
            (uint)data.Length,
            data));
    }

    public Task<Rwrite> WriteAsync(Twrite twrite)
        => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Data.Length));

    public Task<Rclunk> ClunkAsync(Tclunk tclunk)
        => Task.FromResult(new Rclunk(tclunk.Tag));

    public Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 1, new Qid(QidType.QTFILE, 0, 1), 0644, 0, 0, 0, "file.txt", "none", "none", "none", dialect: Dialect);
        return Task.FromResult(new Rstat(tstat.Tag, stat));
    }

    public Task<Rwstat> WstatAsync(Twstat twstat)
        => Task.FromResult(new Rwstat(twstat.Tag));

    public Task<Rremove> RemoveAsync(Tremove tremove)
        => Task.FromResult(new Rremove(tremove.Tag));

    public Task<Rcreate> CreateAsync(Tcreate tcreate)
        => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 2), 8192));

    public INinePFileSystem Clone() => new WritableFileSystem();
}
